using LexicycleCore.Dictionary;
using LexicycleCore.Progress;

namespace LexicycleCore.Tests;

/// <summary>
/// Adding Spanish is meant to be "a filter change", per docs/ROADMAP.md — these prove the
/// plumbing actually keeps two pairs apart: progress, word ids and route ids all overlap
/// by construction (both dictionaries number words from 1), so nothing here may collide.
/// </summary>
public sealed class LanguagePairTests : IAsyncLifetime
{
    private SqliteDictionaryStore _german = null!;
    private SqliteDictionaryStore _spanish = null!;
    private SqliteProgressStore _progress = null!;
    private PracticeSessionFactory _germanFactory = null!;
    private PracticeSessionFactory _spanishFactory = null!;

    public async Task InitializeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lexicycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var germanPath = Path.Combine(folder, "dict-de.db");
        var spanishPath = Path.Combine(folder, "dict-es.db");
        BuildDictionary(germanPath, "de", words: 20);
        BuildDictionary(spanishPath, "es", words: 20);

        _german = new SqliteDictionaryStore(germanPath, "de");
        _spanish = new SqliteDictionaryStore(spanishPath, "es");
        _progress = new SqliteProgressStore(Path.Combine(folder, "progress.db"));
        _germanFactory = new PracticeSessionFactory(LanguagePair.German, _german, _progress);
        _spanishFactory = new PracticeSessionFactory(LanguagePair.Spanish, _spanish, _progress);

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _german.DisposeAsync();
        await _spanish.DisposeAsync();
        await _progress.DisposeAsync();
    }

    private static void BuildDictionary(string path, string targetLanguage, int words)
    {
        using var connection = new SQLite.SQLiteConnection(path);

        connection.Execute("CREATE TABLE words_en (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, pos TEXT, freq_rank INTEGER)");
        connection.Execute($"CREATE TABLE words_{targetLanguage} (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, gender TEXT)");
        connection.Execute($"CREATE TABLE translations (en_id INTEGER NOT NULL, {targetLanguage}_id INTEGER NOT NULL, PRIMARY KEY (en_id, {targetLanguage}_id))");

        for (var i = 1; i <= words; i++)
        {
            // Both dictionaries number their words from 1: any accidental sharing of a
            // progress scope or word-id space would make these collide.
            connection.Execute(
                "INSERT INTO words_en (id, text, pos, freq_rank) VALUES (?, ?, 'noun', ?)",
                i, $"{targetLanguage}word{i:D3}", i * 10);
            connection.Execute(
                $"INSERT INTO words_{targetLanguage} (id, text, gender) VALUES (?, ?, NULL)",
                i, $"{targetLanguage}Wort{i:D3}");
            connection.Execute(
                $"INSERT INTO translations (en_id, {targetLanguage}_id) VALUES (?, ?)", i, i);
        }
    }

    [Fact]
    public void German_keeps_the_legacy_unscoped_progress_key()
    {
        // Real installs already have progress written under the bare "dictionary" key,
        // from before language pairs existed. Renaming it would orphan that history.
        Assert.Equal(ProgressScope.Dictionary, ProgressScope.ForDictionary("en-de"));
    }

    [Fact]
    public void A_second_pair_gets_its_own_progress_scope()
    {
        Assert.NotEqual(ProgressScope.Dictionary, ProgressScope.ForDictionary("en-es"));
    }

    [Fact]
    public void Generated_set_ids_round_trip_to_their_pair()
    {
        var id = PracticeSessionFactory.GeneratedSetIdFor(LanguagePair.Spanish);
        var route = PracticeSessionFactory.PairForGeneratedSetId(id);

        Assert.Equal(LanguagePair.Spanish, route?.Pair);
        Assert.False(route?.Reversed);
        Assert.Null(FrequencyBand.ById(id));
    }

    [Fact]
    public void Reversed_generated_set_ids_round_trip_too()
    {
        var id = PracticeSessionFactory.GeneratedSetIdFor(LanguagePair.German, reversed: true);
        var route = PracticeSessionFactory.PairForGeneratedSetId(id);

        Assert.Equal(LanguagePair.German, route?.Pair);
        Assert.True(route?.Reversed);
    }

    [Fact]
    public async Task Practising_one_pair_never_touches_the_other_s_progress()
    {
        var germanSession = await _germanFactory.CreateAsync(size: 10);
        await _germanFactory.RecordAsync(germanSession, PlayPerfectly(germanSession));

        var germanSeen = await _progress.GetAllAsync(ProgressScope.ForDictionary("en-de"));
        var spanishSeen = await _progress.GetAllAsync(ProgressScope.ForDictionary("en-es"));

        Assert.Equal(10, germanSeen.Count);
        Assert.Empty(spanishSeen);

        // Spanish's own first session still gets the full 80%-new session German just
        // used up its scope for — proof the two really are independent, not merely
        // reporting different numbers off a shared pool.
        var spanishSession = await _spanishFactory.CreateAsync(size: 10);
        Assert.Equal(1, spanishSession.SessionNumber);
        Assert.Equal(10, spanishSession.Set.WordCount);
    }

    [Fact]
    public async Task Each_pair_s_words_carry_its_own_target_language()
    {
        var germanSession = await _germanFactory.CreateAsync(size: 3);
        var spanishSession = await _spanishFactory.CreateAsync(size: 3);

        Assert.Equal("de", germanSession.Set.TargetLanguage);
        Assert.Equal("es", spanishSession.Set.TargetLanguage);
        Assert.All(germanSession.Set.Words, w => Assert.StartsWith("deWort", w.PrimaryAnswer));
        Assert.All(spanishSession.Set.Words, w => Assert.StartsWith("esWort", w.PrimaryAnswer));
    }

    private static Session.SessionSummary PlayPerfectly(PracticeSessionFactory.PracticeSession session)
    {
        var engine = new Session.SessionEngine(session.Set);
        while (!engine.IsComplete)
        {
            engine.Submit(engine.CurrentWord!.PrimaryAnswer);
        }

        return engine.BuildSummary();
    }
}
