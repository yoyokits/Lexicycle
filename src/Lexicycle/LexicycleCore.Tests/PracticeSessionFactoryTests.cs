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
    public async Task Most_of_every_session_is_material_the_learner_has_never_seen()
    {
        var everSeen = new HashSet<string>();

        // Four sessions of ten over a forty-word fixture: the last one still has new
        // material to draw on, so the 80% floor is testable throughout.
        for (var i = 0; i < 4; i++)
        {
            var session = await _factory.CreateAsync(size: 10);
            var fresh = session.Set.Words.Count(word => !everSeen.Contains(word.Source));

            Assert.True(
                fresh >= 8,
                $"session {i + 1} offered only {fresh} new words out of {session.Set.WordCount}");

            foreach (var word in session.Set.Words)
            {
                everSeen.Add(word.Source);
            }

            await _factory.RecordAsync(session, PlayPerfectly(session));
        }
    }

    [Fact]
    public async Task A_missed_word_can_come_back_after_skipping_one_session()
    {
        // Revision is sampled rather than sorted, so a specific word returning is a
        // likelihood, not a certainty. What is certain is that it cannot return in the
        // very next session — and that a heavily failed word does come back eventually.
        var first = await _factory.CreateAsync(size: 10);
        var struggled = first.Set.Words[0].Source;

        await _factory.RecordAsync(first, PlayMissing(first, new HashSet<string> { struggled }));

        var second = await _factory.CreateAsync(size: 10);
        Assert.DoesNotContain(struggled, second.Set.Words.Select(word => word.Source));
        await _factory.RecordAsync(second, PlayPerfectly(second));

        var returned = false;
        for (var i = 0; i < 12 && !returned; i++)
        {
            var session = await _factory.CreateAsync(size: 10);
            returned = session.Set.Words.Any(word => word.Source == struggled);
            await _factory.RecordAsync(session, PlayPerfectly(session));
        }

        Assert.True(returned, $"'{struggled}' never came back in twelve sessions");
    }

    [Fact]
    public async Task No_word_is_ever_repeated_from_the_immediately_previous_session()
    {
        // The property the learner actually notices, played over a realistic run in which
        // a third of each session is missed and so demoted for relearning.
        var rng = new Random(7);
        List<string>? previous = null;

        for (var s = 1; s <= 8; s++)
        {
            var session = await _factory.CreateAsync(size: 4);
            var words = session.Set.Words.Select(word => word.Source).ToList();

            if (previous is not null)
            {
                Assert.Empty(words.Intersect(previous));
            }

            var engine = new SessionEngine(session.Set);
            var missed = new HashSet<string>();
            while (!engine.IsComplete)
            {
                var word = engine.CurrentWord!;
                if (rng.NextDouble() < 0.33 && missed.Add(word.Source))
                {
                    engine.Submit("deliberately wrong");
                }
                else
                {
                    engine.Submit(word.PrimaryAnswer);
                }
            }

            await _factory.RecordAsync(session, engine.BuildSummary());
            previous = words;
        }
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
    public async Task Once_the_dictionary_is_exhausted_sessions_become_pure_revision()
    {
        // 40 words, taken 20 at a time and all answered perfectly.
        List<string> previousWords = [];
        for (var i = 0; i < 2; i++)
        {
            var session = await _factory.CreateAsync(size: 20);
            await _factory.RecordAsync(session, PlayPerfectly(session));
            previousWords = session.Set.Words.Select(w => w.Source).ToList();
        }

        var next = await _factory.CreateAsync(size: 20);

        // The 80% new rule cannot be met with nothing new left, so revision takes the
        // whole session rather than leaving the learner with nothing to do.
        Assert.False(next.IsEmpty);
        Assert.Equal(20, next.Set.WordCount);

        // And the no-repeat rule still holds against the session just played.
        Assert.Empty(next.Set.Words.Select(w => w.Source).Intersect(previousWords));
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
        var first = await _factory.CreateAsync(size: 10);
        var struggled = first.Set.Words[0].Source;
        var struggledId = first.WordIdsBySource[struggled];

        await _factory.RecordAsync(first, PlayMissing(first, new HashSet<string> { struggled }));

        // Nine of the ten were known; the missed one does not count yet.
        Assert.Equal(9, await _progress.CountLearnedAsync());

        // Play on until revision brings it back, then answer it cleanly.
        for (var i = 0; i < 12; i++)
        {
            var session = await _factory.CreateAsync(size: 10);
            await _factory.RecordAsync(session, PlayPerfectly(session));

            if (session.Set.Words.Any(word => word.Source == struggled))
            {
                break;
            }
        }

        var progress = await _progress.GetAllAsync();
        var record = progress.Single(p => p.WordId == struggledId);

        Assert.True(record.TimesCorrect > 0, $"'{struggled}' was never relearned");
        Assert.Equal(1, record.TimesAnsweredWrong);
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
