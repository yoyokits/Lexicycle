using LexicycleCore.Dictionary;
using LexicycleCore.Progress;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

/// <summary>
/// End-to-end over real SQLite files: generate a session, play it, record the results,
/// and confirm the following session asks different words.
/// </summary>
public sealed class PracticeSessionFactoryTests : IAsyncLifetime
{
    private string _dictionaryPath = string.Empty;
    private string _progressPath = string.Empty;
    private SqliteDictionaryStore _dictionary = null!;
    private SqliteProgressStore _progress = null!;
    private PracticeSessionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lexicycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        _dictionaryPath = Path.Combine(folder, "dict.db");
        _progressPath = Path.Combine(folder, "progress.db");

        BuildDictionary(_dictionaryPath, words: 40);

        _dictionary = new SqliteDictionaryStore(_dictionaryPath);
        _progress = new SqliteProgressStore(_progressPath);
        _factory = new PracticeSessionFactory(_dictionary, _progress);

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dictionary.DisposeAsync();
        await _progress.DisposeAsync();
    }

    /// <summary>Writes a dictionary in the shape src/python produces.</summary>
    private static void BuildDictionary(string path, int words)
    {
        using var connection = new SQLite.SQLiteConnection(path);

        connection.Execute("CREATE TABLE words_en (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, pos TEXT, freq_rank INTEGER)");
        connection.Execute("CREATE TABLE words_de (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, gender TEXT)");
        connection.Execute("CREATE TABLE translations (en_id INTEGER NOT NULL, de_id INTEGER NOT NULL, PRIMARY KEY (en_id, de_id))");

        for (var i = 1; i <= words; i++)
        {
            connection.Execute(
                "INSERT INTO words_en (id, text, pos, freq_rank) VALUES (?, ?, 'noun', ?)",
                i, $"word{i:D3}", i * 10);
            connection.Execute(
                "INSERT INTO words_de (id, text, gender) VALUES (?, ?, ?)",
                i, $"Wort{i:D3}", i % 3 == 0 ? "neuter" : null);
            connection.Execute("INSERT INTO translations (en_id, de_id) VALUES (?, ?)", i, i);
        }
    }

    /// <summary>Plays a session through, answering everything correctly first time.</summary>
    private static SessionSummary PlayPerfectly(PracticeSessionFactory.PracticeSession session)
    {
        var engine = new SessionEngine(session.Set);
        while (!engine.IsComplete)
        {
            engine.Submit(engine.CurrentWord!.PrimaryAnswer);
        }

        return engine.BuildSummary();
    }

    /// <summary>Plays a session, deliberately missing the named prompts once each.</summary>
    private static SessionSummary PlayMissing(
        PracticeSessionFactory.PracticeSession session,
        ISet<string> missOnce)
    {
        var engine = new SessionEngine(session.Set);
        var missed = new HashSet<string>();

        while (!engine.IsComplete)
        {
            var word = engine.CurrentWord!;
            if (missOnce.Contains(word.Source) && missed.Add(word.Source))
            {
                engine.Submit("deliberately wrong");
            }
            else
            {
                engine.Submit(word.PrimaryAnswer);
            }
        }

        return engine.BuildSummary();
    }

    [Fact]
    public async Task A_generated_session_has_the_requested_size()
    {
        var session = await _factory.CreateAsync(size: 10);

        Assert.Equal(1, session.SessionNumber);
        Assert.Equal(10, session.Set.WordCount);
        Assert.Equal("en", session.Set.SourceLanguage);
        Assert.Equal("de", session.Set.TargetLanguage);
    }

    [Fact]
    public async Task The_first_session_takes_the_most_frequent_words()
    {
        var session = await _factory.CreateAsync(size: 5);

        Assert.Equal(
            ["word001", "word002", "word003", "word004", "word005"],
            session.Set.Words.Select(word => word.Source));
    }

    [Fact]
    public async Task Gender_becomes_a_hint_that_does_not_reveal_the_answer()
    {
        var session = await _factory.CreateAsync(size: 10);

        var withHint = session.Set.Words.First(word => word.Hint is not null);

        Assert.Equal("das … (neuter)", withHint.Hint);
        Assert.DoesNotContain(withHint.PrimaryAnswer, withHint.Hint!);
    }

    [Fact]
    public async Task The_next_session_asks_different_words()
    {
        // The behaviour this feature exists for.
        var first = await _factory.CreateAsync(size: 10);
        await _factory.RecordAsync(first, PlayPerfectly(first));

        var second = await _factory.CreateAsync(size: 10);

        var firstWords = first.Set.Words.Select(word => word.Source).ToHashSet();
        var secondWords = second.Set.Words.Select(word => word.Source).ToHashSet();

        Assert.Empty(firstWords.Intersect(secondWords));
        Assert.Equal(2, second.SessionNumber);
    }

    [Fact]
    public async Task Progress_survives_reopening_the_store()
    {
        var first = await _factory.CreateAsync(size: 10);
        await _factory.RecordAsync(first, PlayPerfectly(first));
        await _progress.DisposeAsync();

        // A fresh store over the same file, as after an app restart.
        _progress = new SqliteProgressStore(_progressPath);
        _factory = new PracticeSessionFactory(_dictionary, _progress);

        var second = await _factory.CreateAsync(size: 10);

        Assert.Equal(2, second.SessionNumber);
        Assert.Empty(
            first.Set.Words.Select(w => w.Source)
                .Intersect(second.Set.Words.Select(w => w.Source)));
    }

    [Fact]
    public async Task Several_consecutive_sessions_never_repeat_while_new_words_remain()
    {
        var everSeen = new HashSet<string>();

        for (var i = 0; i < 4; i++)
        {
            var session = await _factory.CreateAsync(size: 5);
            foreach (var word in session.Set.Words)
            {
                Assert.True(everSeen.Add(word.Source), $"'{word.Source}' was asked twice.");
            }

            await _factory.RecordAsync(session, PlayPerfectly(session));
        }

        Assert.Equal(20, everSeen.Count);
    }

    [Fact]
    public async Task A_missed_word_comes_back_in_a_later_session()
    {
        var first = await _factory.CreateAsync(size: 4);
        var struggled = first.Set.Words[0].Source;

        await _factory.RecordAsync(first, PlayMissing(first, new HashSet<string> { struggled }));

        // A missed word drops to box 0, so it is due the very next session.
        var second = await _factory.CreateAsync(size: 4);

        Assert.Contains(struggled, second.Set.Words.Select(word => word.Source));
    }

    [Fact]
    public async Task A_word_answered_correctly_does_not_come_back_immediately()
    {
        var first = await _factory.CreateAsync(size: 4);
        await _factory.RecordAsync(first, PlayPerfectly(first));

        var second = await _factory.CreateAsync(size: 4);

        Assert.Empty(
            first.Set.Words.Select(w => w.Source)
                .Intersect(second.Set.Words.Select(w => w.Source)));
    }

    [Fact]
    public async Task Sessions_shrink_rather_than_repeat_once_the_dictionary_is_exhausted()
    {
        // 40 words, taken 20 at a time and all answered perfectly.
        for (var i = 0; i < 2; i++)
        {
            var session = await _factory.CreateAsync(size: 20);
            await _factory.RecordAsync(session, PlayPerfectly(session));
        }

        var next = await _factory.CreateAsync(size: 20);

        // Nothing new is left and nothing is due yet, so there is simply nothing to ask.
        Assert.True(next.IsEmpty);
    }

    [Fact]
    public async Task Recording_counts_misses_against_the_word()
    {
        var session = await _factory.CreateAsync(size: 3);
        var struggled = session.Set.Words[1].Source;

        await _factory.RecordAsync(session, PlayMissing(session, new HashSet<string> { struggled }));

        var progress = await _progress.GetAllAsync();
        var record = progress.Single(p => p.WordId == session.WordIdsBySource[struggled]);

        Assert.Equal(1, record.TimesMissed);
        Assert.Equal(0, record.TimesCorrect);
        Assert.Equal(0, record.Box);  // demoted for relearning
    }

    [Fact]
    public async Task Only_words_answered_correctly_count_towards_a_milestone()
    {
        var session = await _factory.CreateAsync(size: 5);
        var struggled = session.Set.Words[0].Source;

        Assert.Equal(0, await _progress.CountLearnedAsync());

        await _factory.RecordAsync(session, PlayMissing(session, new HashSet<string> { struggled }));

        // Five words were asked, but the missed one was relearned rather than known.
        Assert.Equal(4, await _progress.CountLearnedAsync());
    }

    [Fact]
    public async Task Relearning_a_missed_word_later_adds_it_to_the_count()
    {
        var first = await _factory.CreateAsync(size: 4);
        var struggled = first.Set.Words[0].Source;
        await _factory.RecordAsync(first, PlayMissing(first, new HashSet<string> { struggled }));

        Assert.Equal(3, await _progress.CountLearnedAsync());

        // Box 0 brings it straight back; getting it right this time promotes it.
        var second = await _factory.CreateAsync(size: 4);
        Assert.Contains(struggled, second.Set.Words.Select(word => word.Source));
        await _factory.RecordAsync(second, PlayPerfectly(second));

        Assert.Equal(7, await _progress.CountLearnedAsync());
    }

    [Fact]
    public async Task The_learned_count_does_not_double_count_a_repeated_word()
    {
        var first = await _factory.CreateAsync(size: 4);
        var struggled = first.Set.Words[0].Source;

        // Miss it, then get it right twice over the next two sessions.
        await _factory.RecordAsync(first, PlayMissing(first, new HashSet<string> { struggled }));
        var second = await _factory.CreateAsync(size: 4);
        await _factory.RecordAsync(second, PlayPerfectly(second));

        var learned = await _progress.CountLearnedAsync();
        var distinct = (await _progress.GetAllAsync()).Count(p => p.TimesCorrect > 0);

        Assert.Equal(distinct, learned);
    }

    [Fact]
    public async Task Resetting_progress_clears_the_learned_count()
    {
        var session = await _factory.CreateAsync(size: 5);
        await _factory.RecordAsync(session, PlayPerfectly(session));
        Assert.Equal(5, await _progress.CountLearnedAsync());

        await _progress.ResetAsync();

        Assert.Equal(0, await _progress.CountLearnedAsync());
    }

    [Fact]
    public async Task Resetting_progress_starts_the_rotation_over()
    {
        var first = await _factory.CreateAsync(size: 5);
        await _factory.RecordAsync(first, PlayPerfectly(first));

        await _progress.ResetAsync();
        var afterReset = await _factory.CreateAsync(size: 5);

        Assert.Equal(1, afterReset.SessionNumber);
        Assert.Equal(
            first.Set.Words.Select(w => w.Source),
            afterReset.Set.Words.Select(w => w.Source));
    }
}
