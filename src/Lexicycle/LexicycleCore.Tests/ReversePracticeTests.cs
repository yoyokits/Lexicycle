using LexicycleCore.Dictionary;
using LexicycleCore.Progress;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

/// <summary>
/// R-305: practising German → English as well as English → German, over a fixture shaped
/// like the real data's rough edges — a German word that answers more than one English
/// prompt, and an English prompt with more than one German answer — rather than the tidy
/// one-to-one fixture used elsewhere, so the ambiguity this direction actually has to
/// handle is exercised rather than assumed away.
/// </summary>
public sealed class ReversePracticeTests : IAsyncLifetime
{
    private SqliteDictionaryStore _dictionary = null!;
    private SqliteProgressStore _progress = null!;
    private PracticeSessionFactory _forward = null!;
    private PracticeSessionFactory _reverse = null!;

    public async Task InitializeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lexicycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var dictionaryPath = Path.Combine(folder, "dict.db");
        BuildDictionary(dictionaryPath);

        _dictionary = new SqliteDictionaryStore(dictionaryPath, "de");
        _progress = new SqliteProgressStore(Path.Combine(folder, "progress.db"));
        _forward = new PracticeSessionFactory(LanguagePair.German, _dictionary, _progress);
        _reverse = new PracticeSessionFactory(LanguagePair.German, _dictionary, _progress, reversed: true);

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dictionary.DisposeAsync();
        await _progress.DisposeAsync();
    }

    /// <summary>
    /// English: house, cat, office, trunk, ... (20 words, freq_rank = index * 10).
    /// German: mirrors real ambiguity —
    ///   "Amt" answers both "office" and "trunk" (genuine polysemy, kept as multiple
    ///   acceptable answers in reverse, exactly as multiple German answers are already
    ///   kept for one English prompt going forward).
    ///   "Haus" is the sole answer for "house"; "Katze" the sole answer for "cat".
    /// </summary>
    private static void BuildDictionary(string path)
    {
        using var connection = new SQLite.SQLiteConnection(path);

        connection.Execute("CREATE TABLE words_en (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, pos TEXT, freq_rank INTEGER)");
        connection.Execute("CREATE TABLE words_de (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, gender TEXT)");
        connection.Execute("CREATE TABLE translations (en_id INTEGER NOT NULL, de_id INTEGER NOT NULL, PRIMARY KEY (en_id, de_id))");

        // Plain one-to-one filler, words 3..20.
        for (var i = 3; i <= 20; i++)
        {
            connection.Execute(
                "INSERT INTO words_en (id, text, pos, freq_rank) VALUES (?, ?, 'noun', ?)",
                i, $"word{i:D3}", i * 10);
            connection.Execute(
                "INSERT INTO words_de (id, text, gender) VALUES (?, ?, ?)",
                i, $"Wort{i:D3}", i % 3 == 0 ? "neuter" : null);
            connection.Execute("INSERT INTO translations (en_id, de_id) VALUES (?, ?)", i, i);
        }

        // house (rank 10) -> Haus.
        connection.Execute("INSERT INTO words_en (id, text, pos, freq_rank) VALUES (1, 'house', 'noun', 10)");
        connection.Execute("INSERT INTO words_de (id, text, gender) VALUES (1, 'Haus', 'neuter')");
        connection.Execute("INSERT INTO translations (en_id, de_id) VALUES (1, 1)");

        // office (rank 20) and trunk (rank 200) both answer "Amt" (id 2) — the ambiguous
        // German word. office is far more common, so it should sort first among Amt's
        // reverse answers and drive Amt's own approximate frequency rank.
        connection.Execute("INSERT INTO words_en (id, text, pos, freq_rank) VALUES (2, 'office', 'noun', 20)");
        connection.Execute("INSERT INTO words_en (id, text, pos, freq_rank) VALUES (21, 'trunk', 'noun', 200)");
        connection.Execute("INSERT INTO words_de (id, text, gender) VALUES (2, 'Amt', 'neuter')");
        connection.Execute("INSERT INTO translations (en_id, de_id) VALUES (2, 2)");
        connection.Execute("INSERT INTO translations (en_id, de_id) VALUES (21, 2)");
    }

    private static SessionSummary PlayPerfectly(PracticeSessionFactory.PracticeSession session)
    {
        var engine = new SessionEngine(session.Set);
        while (!engine.IsComplete)
        {
            engine.Submit(engine.CurrentWord!.PrimaryAnswer);
        }

        return engine.BuildSummary();
    }

    [Fact]
    public async Task A_reversed_session_prompts_with_the_target_language_word()
    {
        var session = await _reverse.CreateAsync(size: 3);

        // Most-common-first by the reverse heuristic: Haus (via house, rank 10), Amt
        // (via its best-ranked answer office, rank 20), Wort003 (rank 30).
        Assert.Equal(["Haus", "Amt", "Wort003"], session.Set.Words.Select(w => w.Source));
        Assert.Equal("de", session.Set.SourceLanguage);
        Assert.Equal("en", session.Set.TargetLanguage);
    }

    [Fact]
    public async Task A_genuinely_ambiguous_word_accepts_every_correct_answer()
    {
        var session = await _reverse.CreateAsync(size: 2);
        var amt = session.Set.Words.Single(w => w.Source == "Amt");

        Assert.Equal(["office", "trunk"], amt.AcceptableAnswers);
    }

    [Fact]
    public async Task Reversed_questions_carry_no_gender_hint()
    {
        // Haus is neuter and gets a hint going forward; reversed, there is nothing German
        // grammar can hint about an English answer.
        var session = await _reverse.CreateAsync(size: 1);

        Assert.Null(session.Set.Words.Single().Hint);
    }

    [Fact]
    public async Task Forward_and_reverse_practice_of_the_same_pair_have_independent_progress()
    {
        var forwardSession = await _forward.CreateAsync(size: 5);
        await _forward.RecordAsync(forwardSession, PlayPerfectly(forwardSession));

        var forwardSeen = await _progress.GetAllAsync(ProgressScope.ForDictionary("en-de", reversed: false));
        var reverseSeen = await _progress.GetAllAsync(ProgressScope.ForDictionary("en-de", reversed: true));

        Assert.Equal(5, forwardSeen.Count);
        Assert.Empty(reverseSeen);

        // Reverse's own first session still draws a full new session — proof the pools
        // are genuinely separate id spaces, not just separately labelled.
        var reverseSession = await _reverse.CreateAsync(size: 5);
        Assert.Equal(1, reverseSession.SessionNumber);
        Assert.Equal(5, reverseSession.Set.WordCount);
    }

    [Fact]
    public async Task Reverse_word_ids_are_target_language_ids_not_english_ids()
    {
        // "Amt" is German word id 2, which collides with English word id 2 ("office").
        // Recording a reverse outcome must write against the German id, not the English
        // one the forward direction would have used for a different word entirely.
        var session = await _reverse.CreateAsync(size: 2);
        var amtId = session.WordIdsBySource["Amt"];

        Assert.Equal(2, amtId);

        await _reverse.RecordAsync(session, PlayPerfectly(session));

        var seen = await _progress.GetAllAsync(ProgressScope.ForDictionary("en-de", reversed: true));
        Assert.Contains(seen, p => p.WordId == 2);
    }

    [Fact]
    public async Task No_reverse_word_is_ever_repeated_from_the_immediately_previous_session()
    {
        var first = await _reverse.CreateAsync(size: 3);
        await _reverse.RecordAsync(first, PlayPerfectly(first));

        var second = await _reverse.CreateAsync(size: 3);

        Assert.Empty(
            first.Set.Words.Select(w => w.Source).Intersect(second.Set.Words.Select(w => w.Source)));
    }

    [Fact]
    public void Reverse_bands_are_windows_over_the_reverse_ordering_with_their_own_ids()
    {
        var forwardBasics = FrequencyBand.For(LanguagePair.German)[0];
        var reverseBasics = FrequencyBand.For(LanguagePair.German, reversed: true)[0];

        Assert.NotEqual(forwardBasics.Id, reverseBasics.Id);
        Assert.Equal("en-de:reverse:basics", reverseBasics.Id);
        Assert.True(reverseBasics.Reversed);
        Assert.False(forwardBasics.Reversed);
        Assert.Equal(reverseBasics, FrequencyBand.ById(reverseBasics.Id));
    }

    [Fact]
    public async Task A_reverse_band_session_only_draws_from_inside_the_band()
    {
        var band = new FrequencyBand("tiny-reverse", "Tiny", Skip: 0, Size: 2, PairId: "en-de", Reversed: true);

        var session = await _reverse.CreateAsync(size: 2, band: band);

        // Only Haus and Amt rank inside the first two reverse-ordered words.
        Assert.Equal(["Haus", "Amt"], session.Set.Words.Select(w => w.Source));
    }
}
