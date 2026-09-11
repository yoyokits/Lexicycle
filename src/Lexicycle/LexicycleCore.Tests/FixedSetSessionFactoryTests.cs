using LexicycleCore.Dictionary;
using LexicycleCore.Models;
using LexicycleCore.Progress;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

/// <summary>
/// Opening a bundled set repeatedly must not replay the same questions. Run over a real
/// SQLite progress file, because that is where the rotation is actually remembered.
/// </summary>
public sealed class FixedSetSessionFactoryTests : IAsyncLifetime
{
    private SqliteProgressStore _progress = null!;
    private FixedSetSessionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lexicycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        _progress = new SqliteProgressStore(Path.Combine(folder, "progress.db"));
        _factory = new FixedSetSessionFactory(_progress);

        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _progress.DisposeAsync();

    private static VocabularySet SetOf(int words, string id = "en-de-basics") => new(
        id,
        "German basics",
        "en",
        "de",
        [.. Enumerable.Range(1, words).Select(i => new WordPair($"w{i:D2}", [$"W{i:D2}"]))]);

    private static SessionSummary PlayPerfectly(VocabularySet set)
    {
        var engine = new SessionEngine(set);
        while (!engine.IsComplete)
        {
            engine.Submit(engine.CurrentWord!.PrimaryAnswer);
        }

        return engine.BuildSummary();
    }

    /// <summary>Opens the set, plays it through, and returns what it asked.</summary>
    private async Task<List<string>> VisitAsync(VocabularySet set)
    {
        var session = await _factory.CreateAsync(set);
        var asked = session.Set.Words.Select(word => word.Source).ToList();

        if (asked.Count > 0)
        {
            await _factory.RecordAsync(session, PlayPerfectly(session.Set));
        }

        return asked;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(12)]
    [InlineData(20)]
    public async Task Tapping_a_set_over_and_over_never_asks_the_same_word_twice(int words)
    {
        // The reported bug, checked at the size that makes it obvious: tap the set,
        // finish it, tap it again. Before this, every word came back every single time.
        //
        // Not merely "no repeat back to back" — no repeat at all, ever. Once the set is
        // used up the sessions are empty and the learner is told to restart it.
        var set = SetOf(words);
        var everAsked = new List<string>();

        for (var visit = 1; visit <= 12; visit++)
        {
            var asked = await VisitAsync(set);

            foreach (var word in asked)
            {
                Assert.DoesNotContain(word, everAsked);
                everAsked.Add(word);
            }
        }

        // Everything was reached, nothing stranded, nothing repeated.
        Assert.Equal(words, everAsked.Count);
        Assert.Equal(words, everAsked.Distinct().Count());
    }

    [Fact]
    public async Task The_second_visit_is_entirely_different_material()
    {
        // Half the set at a time, so the follow-up visit is all words the first one
        // did not ask, rather than a handful of leftovers.
        var set = SetOf(12);

        var first = await VisitAsync(set);
        var second = await VisitAsync(set);

        Assert.Equal(6, first.Count);
        Assert.Equal(6, second.Count);
        Assert.Empty(first.Intersect(second));
        Assert.Equal(12, first.Concat(second).Distinct().Count());
    }

    [Fact]
    public async Task An_exhausted_set_yields_an_empty_session_until_it_is_restarted()
    {
        var set = SetOf(12);

        await VisitAsync(set);
        await VisitAsync(set);

        // Every word has been asked, so there is nothing left that would not be a repeat.
        var exhausted = await _factory.CreateAsync(set);
        Assert.True(exhausted.IsEmpty);

        // Restarting clears just this set and makes its words new again.
        await _progress.ResetScopeAsync(ProgressScope.ForSet(set.Id));

        var restarted = await _factory.CreateAsync(set);
        Assert.Equal(6, restarted.Set.WordCount);
        Assert.Equal(1, restarted.SessionNumber);
    }

    [Fact]
    public async Task Restarting_one_set_leaves_other_scopes_alone()
    {
        var german = SetOf(12, "en-de-basics");
        var spanish = SetOf(12, "en-es-basics");

        await VisitAsync(german);
        await VisitAsync(spanish);

        await _progress.ResetScopeAsync(ProgressScope.ForSet(german.Id));

        Assert.Empty(await _progress.GetAllAsync(ProgressScope.ForSet(german.Id)));
        Assert.NotEmpty(await _progress.GetAllAsync(ProgressScope.ForSet(spanish.Id)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(12, 6)]
    [InlineData(20, 10)]
    [InlineData(100, 10)]   // capped at the default session size
    public void Session_size_is_half_the_set_up_to_the_default(int words, int expected)
        => Assert.Equal(expected, FixedSetSessionFactory.SizeFor(words));

    [Fact]
    public async Task Two_different_sets_keep_separate_rotations()
    {
        // Scoping by set id: practising one set must not advance another's rotation, or
        // its no-repeat guarantee would be satisfied by sessions never played.
        var german = SetOf(12, "en-de-basics");
        var spanish = SetOf(12, "en-es-basics");

        var germanFirst = await VisitAsync(german);
        await VisitAsync(spanish);
        var germanSecond = await VisitAsync(german);

        Assert.Empty(germanFirst.Intersect(germanSecond));
    }

    [Fact]
    public async Task A_fixed_set_does_not_disturb_dictionary_progress()
    {
        await VisitAsync(SetOf(12));

        // The milestone bar counts the dictionary; a bundled set is not progress through it.
        Assert.Equal(0, await _progress.CountLearnedAsync(ProgressScope.Dictionary));
        Assert.Empty(await _progress.GetAllAsync(ProgressScope.Dictionary));
    }

    [Fact]
    public async Task A_single_word_set_is_still_playable()
    {
        // Nothing can rotate, but it must not return an empty session either.
        var set = SetOf(1);

        Assert.Single(await VisitAsync(set));
    }
}
