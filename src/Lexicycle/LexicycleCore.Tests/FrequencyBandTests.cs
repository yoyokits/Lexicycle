using LexicycleCore.Dictionary;
using LexicycleCore.Progress;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

/// <summary>
/// Bands replaced the twelve-word starter sets. The point of them is that they are large
/// enough to be worth working through, and that they slice one shared body of vocabulary
/// rather than duplicating it.
/// </summary>
public sealed class FrequencyBandTests : IAsyncLifetime
{
    private const int Words = 2_500;

    private SqliteDictionaryStore _dictionary = null!;
    private SqliteProgressStore _progress = null!;
    private PracticeSessionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lexicycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var dictionaryPath = Path.Combine(folder, "dict.db");
        using (var connection = new SQLite.SQLiteConnection(dictionaryPath))
        {
            connection.Execute("CREATE TABLE words_en (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, pos TEXT, freq_rank INTEGER)");
            connection.Execute("CREATE TABLE words_de (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE, gender TEXT)");
            connection.Execute("CREATE TABLE translations (en_id INTEGER NOT NULL, de_id INTEGER NOT NULL, PRIMARY KEY (en_id, de_id))");

            // One transaction: 2,500 autocommitted inserts per test made the suite crawl.
            connection.RunInTransaction(() =>
            {
                for (var i = 1; i <= Words; i++)
                {
                    // Shaped like the real column: a scaled Zipf score, not an ordinal.
                    // src/python writes round((8 - zipf) * 1000), which lands in the
                    // 1,590-6,990 range with heavy ties — five words share a score here.
                    var score = 1_590 + (i - 1) / 5 * 10;

                    connection.Execute(
                        "INSERT INTO words_en (id, text, pos, freq_rank) VALUES (?, ?, 'noun', ?)",
                        i, $"w{i:D5}", score);
                    connection.Execute("INSERT INTO words_de (id, text, gender) VALUES (?, ?, NULL)", i, $"W{i:D5}");
                    connection.Execute("INSERT INTO translations (en_id, de_id) VALUES (?, ?)", i, i);
                }
            });
        }

        _dictionary = new SqliteDictionaryStore(dictionaryPath, "de");
        _progress = new SqliteProgressStore(Path.Combine(folder, "progress.db"));
        _factory = new PracticeSessionFactory(LanguagePair.German, _dictionary, _progress);

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dictionary.DisposeAsync();
        await _progress.DisposeAsync();
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
    public void The_first_band_is_a_thousand_words_not_a_dozen()
    {
        // The whole reason bands exist: a starter set a learner cannot exhaust in two
        // sittings. Twelve-word sets were the thing being replaced.
        var basics = FrequencyBand.For(LanguagePair.German)[0];

        Assert.Equal(0, basics.Skip);
        Assert.Equal(1_000, basics.Size);
        Assert.All(FrequencyBand.For(LanguagePair.German), band => Assert.True(
            band.Size is null or >= 1_000,
            $"band '{band.Name}' holds fewer than 1,000 words"));
    }

    [Fact]
    public void Bands_cover_the_ordering_without_gaps_or_overlaps()
    {
        var expectedSkip = 0;
        foreach (var band in FrequencyBand.For(LanguagePair.German))
        {
            Assert.Equal(expectedSkip, band.Skip);
            expectedSkip += band.Size ?? int.MaxValue;
        }

        // The last band is open-ended, so nothing falls off whatever the dictionary size.
        Assert.Null(FrequencyBand.For(LanguagePair.German)[^1].Size);
    }

    [Fact]
    public async Task Bands_slice_by_position_not_by_the_freq_rank_value()
    {
        // freq_rank is a scaled Zipf score — about 1,590 to 6,990, heavily tied — not an
        // ordinal. Slicing on its value put all 3,545 words in the last band and left
        // Basics empty on the real dictionary. Every word must land in exactly one band.
        var total = 0;
        foreach (var band in FrequencyBand.For(LanguagePair.German))
        {
            total += await _dictionary.CountInBandAsync(band);
        }

        Assert.Equal(Words, total);

        // No word is in two bands, and none is missed.
        var everySeen = new HashSet<int>();
        foreach (var band in FrequencyBand.For(LanguagePair.German))
        {
            var ids = await _dictionary.GetUnseenIdsAsync([], limit: Words, band);
            foreach (var id in ids)
            {
                Assert.True(everySeen.Add(id), $"word {id} is in more than one band");
            }
        }

        Assert.Equal(Words, everySeen.Count);
    }

    [Fact]
    public async Task A_band_counts_only_its_own_slice()
    {
        Assert.Equal(1_000, await _dictionary.CountInBandAsync(FrequencyBand.For(LanguagePair.German)[0]));
        Assert.Equal(1_000, await _dictionary.CountInBandAsync(FrequencyBand.For(LanguagePair.German)[1]));
        Assert.Equal(500, await _dictionary.CountInBandAsync(FrequencyBand.For(LanguagePair.German)[2]));
    }

    [Fact]
    public async Task A_band_session_only_draws_from_inside_the_band()
    {
        var second = FrequencyBand.For(LanguagePair.German)[1];   // ranks 1001-2000

        var session = await _factory.CreateAsync(size: 10, band: second);

        Assert.Equal(10, session.Set.WordCount);
        Assert.All(
            session.Set.Words,
            word => Assert.InRange(int.Parse(word.Source[1..]), 1_001, 2_000));
    }

    [Fact]
    public async Task A_band_starts_at_its_most_common_word()
    {
        var session = await _factory.CreateAsync(size: 3, band: FrequencyBand.For(LanguagePair.German)[1]);

        Assert.Equal(["w01001", "w01002", "w01003"], session.Set.Words.Select(w => w.Source));
    }

    [Fact]
    public async Task Progress_is_shared_across_bands_and_practice()
    {
        // Bands are views over one body of vocabulary, not separate courses. A word
        // learned in Basics must never be offered again as *new* under Practice. It may
        // still come back as revision, which is a different thing and the point of rule 2.
        var basics = await _factory.CreateAsync(size: 10, band: FrequencyBand.For(LanguagePair.German)[0]);
        var learnedIds = basics.WordIdsBySource.Values.ToHashSet();
        await _factory.RecordAsync(basics, PlayPerfectly(basics));

        var seen = await _progress.GetAllAsync(ProgressScope.Dictionary);
        var unseen = await _dictionary.GetUnseenIdsAsync(
            seen.Select(p => p.WordId).ToHashSet(), limit: 10);

        var plan = new SessionComposer(new Random(1)).Compose(
            sessionNumber: 3, size: 10, seen, unseen);

        Assert.Empty(plan.NewWordIds.Intersect(learnedIds));

        // Eight new plus the two-word revision slice; the revision may well be the
        // band's words, which is exactly what sharing progress is for.
        Assert.Equal(8, plan.NewWordIds.Count);
        Assert.All(plan.ReviewWordIds, id => Assert.Contains(id, learnedIds));
    }

    [Fact]
    public async Task An_exhausted_band_yields_an_empty_session()
    {
        // Work through the whole first band, then ask for one more.
        var band = new FrequencyBand("tiny", "Tiny", Skip: 0, Size: 20, PairId: LanguagePair.German.Id);

        for (var i = 0; i < 2; i++)
        {
            var session = await _factory.CreateAsync(size: 10, band: band);
            await _factory.RecordAsync(session, PlayPerfectly(session));
        }

        var next = await _factory.CreateAsync(size: 10, band: band);

        Assert.True(next.IsEmpty);
    }

    [Fact]
    public async Task Finishing_one_band_leaves_the_next_untouched()
    {
        var first = new FrequencyBand("a", "A", Skip: 0, Size: 10, PairId: LanguagePair.German.Id);    // words 1-10
        var second = new FrequencyBand("b", "B", Skip: 10, Size: 10, PairId: LanguagePair.German.Id);  // words 11-20

        var session = await _factory.CreateAsync(size: 10, band: first);
        await _factory.RecordAsync(session, PlayPerfectly(session));

        var next = await _factory.CreateAsync(size: 10, band: second);

        Assert.Equal(10, next.Set.WordCount);
        Assert.All(next.Set.Words, word => Assert.InRange(int.Parse(word.Source[1..]), 11, 20));
    }

    [Theory]
    [InlineData("en-de:basics", "Basics")]
    [InlineData("en-de:common", "Common words")]
    [InlineData("en-de:wider", "Wider vocabulary")]
    [InlineData("en-es:basics", "Basics")]
    public void Bands_resolve_by_id(string id, string expectedName)
        => Assert.Equal(expectedName, FrequencyBand.ById(id)?.Name);

    [Fact]
    public void An_unknown_id_resolves_to_nothing()
        => Assert.Null(FrequencyBand.ById("band-1"));

    [Fact]
    public void Every_pair_gets_its_own_bands_with_globally_unique_ids()
    {
        // Two pairs sharing a bare band id ("basics") would let a route parameter for
        // one language's band resolve to the other's — the id has to carry the pair too.
        var ids = FrequencyBand.All.Select(band => band.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(LanguagePair.All.Count * 3, ids.Count);
        Assert.All(FrequencyBand.All, band => Assert.NotNull(LanguagePair.ById(band.PairId)));
    }
}
