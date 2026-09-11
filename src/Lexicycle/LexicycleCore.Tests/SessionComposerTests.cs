using LexicycleCore.Progress;

namespace LexicycleCore.Tests;

public class SessionComposerTests
{
    /// <summary>Seeded, so a weighted-random policy still gives repeatable tests.</summary>
    private readonly SessionComposer _composer = new(new Random(1234));

    private static WordProgress Seen(int id, int lastSession, int missed = 0) =>
        new(id, Box: 0, TimesSeen: 1, TimesCorrect: 0, TimesMissed: missed, LastSession: lastSession);

    private static IEnumerable<int> Unseen(int from, int count) => Enumerable.Range(from, count);

    // --- the 80% new rule ------------------------------------------------------------

    [Fact]
    public void A_first_session_is_all_new_words()
    {
        var plan = _composer.Compose(sessionNumber: 1, size: 10, seen: [], unseenWordIds: Unseen(1, 500));

        Assert.Empty(plan.ReviewWordIds);
        Assert.Equal(10, plan.NewWordIds.Count);
        Assert.Equal(Enumerable.Range(1, 10), plan.NewWordIds);
    }

    [Fact]
    public void At_least_eighty_percent_of_a_session_is_new()
    {
        // Plenty of revision available, all of it eligible.
        var seen = Enumerable.Range(1, 50).Select(id => Seen(id, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 20, size: 10, seen, Unseen(100, 500));

        Assert.Equal(8, plan.NewWordIds.Count);
        Assert.Equal(2, plan.ReviewWordIds.Count);
        Assert.Equal(10, plan.Count);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(10, 8)]
    [InlineData(20, 16)]
    [InlineData(3, 3)]   // ceil(2.4) = 3, so a tiny session is entirely new
    public void The_new_share_holds_at_every_session_size(int size, int expectedNew)
    {
        var seen = Enumerable.Range(1, 50).Select(id => Seen(id, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 20, size, seen, Unseen(100, 500));

        Assert.Equal(expectedNew, plan.NewWordIds.Count);
        Assert.True(
            plan.NewWordIds.Count >= size * SessionComposer.MinNewShare,
            $"{plan.NewWordIds.Count} of {size} is below the {SessionComposer.MinNewShare:P0} floor");
    }

    [Fact]
    public void New_words_are_taken_in_the_order_supplied()
    {
        // The source yields the most frequent words first, so useful material comes first.
        var plan = _composer.Compose(1, 5, [], Unseen(100, 500));

        Assert.Equal([100, 101, 102, 103, 104], plan.NewWordIds);
    }

    [Fact]
    public void Only_as_many_new_words_as_needed_are_taken_from_the_source()
    {
        // The source may be a lazy query over thousands of rows.
        var taken = 0;

        IEnumerable<int> Counting()
        {
            for (var id = 1; ; id++)
            {
                taken++;
                yield return id;
            }
        }

        _composer.Compose(1, 10, [], Counting());

        Assert.Equal(10, taken);
    }

    // --- never twice running ---------------------------------------------------------

    [Fact]
    public void Words_answered_last_session_are_not_asked_again()
    {
        // The complaint this rule exists for: session 2 must not repeat session 1.
        var seen = Enumerable.Range(1, 10).Select(id => Seen(id, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 2, size: 10, seen, Unseen(11, 500));

        Assert.Empty(plan.ReviewWordIds);
        Assert.DoesNotContain(plan.AllWordIds, id => id <= 10);
    }

    [Fact]
    public void A_heavily_failed_word_is_still_barred_from_the_very_next_session()
    {
        // Failure weighting must never override the no-repeat rule.
        var seen = new[] { Seen(1, lastSession: 4, missed: 99) };

        var plan = _composer.Compose(sessionNumber: 5, size: 10, seen, Unseen(100, 500));

        Assert.DoesNotContain(1, plan.AllWordIds);
    }

    [Fact]
    public void A_word_becomes_eligible_again_the_session_after_next()
    {
        var seen = new[] { Seen(1, lastSession: 4, missed: 5) };

        var plan = _composer.Compose(sessionNumber: 6, size: 10, seen, Unseen(100, 500));

        Assert.Contains(1, plan.ReviewWordIds);
    }

    // --- failure weighting -----------------------------------------------------------

    [Fact]
    public void Revision_favours_the_words_answered_wrong_most_often()
    {
        var seen = new[]
        {
            Seen(1, lastSession: 1, missed: 0),
            Seen(2, lastSession: 1, missed: 0),
            Seen(3, lastSession: 1, missed: 20),   // by far the most trouble
        };

        var picks = 0;
        for (var i = 0; i < 200; i++)
        {
            var composer = new SessionComposer(new Random(i));
            var plan = composer.Compose(sessionNumber: 10, size: 5, seen, Unseen(100, 500));
            if (plan.ReviewWordIds.Contains(3))
            {
                picks++;
            }
        }

        // Weight 21 against 1 and 1: it should dominate, without being guaranteed.
        Assert.True(picks > 140, $"the most-failed word was chosen only {picks} times in 200");
    }

    [Fact]
    public void A_troublesome_word_does_not_appear_in_every_single_session()
    {
        // The reason revision is sampled rather than sorted: taking the worst words
        // outright would serve the same handful forever.
        var seen = new[]
        {
            Seen(1, lastSession: 1, missed: 20),
            Seen(2, lastSession: 1, missed: 1),
            Seen(3, lastSession: 1, missed: 1),
            Seen(4, lastSession: 1, missed: 1),
        };

        var skipped = 0;
        for (var i = 0; i < 200; i++)
        {
            var composer = new SessionComposer(new Random(i));
            var plan = composer.Compose(sessionNumber: 10, size: 5, seen, Unseen(100, 500));
            if (!plan.ReviewWordIds.Contains(1))
            {
                skipped++;
            }
        }

        Assert.True(skipped > 0, "the most-failed word was in all 200 sessions");
    }

    [Fact]
    public void A_word_never_answered_wrong_can_still_come_back()
    {
        // Weight floor: a perfect word is unlikely, not impossible.
        var seen = new[]
        {
            Seen(1, lastSession: 1, missed: 0),
            Seen(2, lastSession: 1, missed: 3),
        };

        var chosen = false;
        for (var i = 0; i < 200 && !chosen; i++)
        {
            var composer = new SessionComposer(new Random(i));
            chosen = composer
                .Compose(sessionNumber: 10, size: 5, seen, Unseen(100, 500))
                .ReviewWordIds.Contains(1);
        }

        Assert.True(chosen, "a word with no failures was never revised in 200 sessions");
    }

    [Fact]
    public void A_word_is_never_drawn_twice_into_one_session()
    {
        var seen = Enumerable.Range(1, 30).Select(id => Seen(id, lastSession: 1, missed: 5)).ToList();

        for (var i = 0; i < 50; i++)
        {
            var composer = new SessionComposer(new Random(i));
            var plan = composer.Compose(sessionNumber: 10, size: 20, seen, Unseen(100, 500));

            Assert.Equal(plan.AllWordIds.Count, plan.AllWordIds.Distinct().Count());
        }
    }

    // --- running out of material -----------------------------------------------------

    [Fact]
    public void Revision_fills_the_session_once_the_dictionary_runs_out()
    {
        // With no new words left there is no reason to leave slots unused.
        var seen = Enumerable.Range(1, 20).Select(id => Seen(id, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 10, size: 10, seen, unseenWordIds: []);

        Assert.Equal(10, plan.ReviewWordIds.Count);
        Assert.Equal(10, plan.ReviewWordIds.Distinct().Count());
    }

    [Fact]
    public void New_words_fill_the_session_when_revision_is_barred()
    {
        // Everything seen was asked last session, so none of it is eligible; the session
        // should still be full rather than short.
        var seen = Enumerable.Range(1, 20).Select(id => Seen(id, lastSession: 9)).ToList();

        var plan = _composer.Compose(sessionNumber: 10, size: 10, seen, Unseen(100, 500));

        Assert.Empty(plan.ReviewWordIds);
        Assert.Equal(10, plan.NewWordIds.Count);
    }

    [Fact]
    public void A_short_session_is_returned_when_nothing_else_is_available()
    {
        var plan = _composer.Compose(sessionNumber: 5, size: 10, seen: [], unseenWordIds: Unseen(1, 3));

        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void An_exhausted_dictionary_with_nothing_eligible_yields_an_empty_plan()
    {
        // The only word known was asked last session, so there is genuinely nothing to ask.
        var seen = new[] { Seen(1, lastSession: 10) };

        var plan = _composer.Compose(sessionNumber: 11, size: 10, seen, unseenWordIds: []);

        Assert.Equal(0, plan.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_size_yields_an_empty_plan(int size)
    {
        Assert.Equal(0, _composer.Compose(1, size, [], Unseen(1, 10)).Count);
    }
}
