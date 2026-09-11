using LexicycleCore.Progress;

namespace LexicycleCore.Tests;

public class SessionComposerTests
{
    private readonly SessionComposer _composer = new();

    private static WordProgress Seen(int id, int box, int lastSession, int missed = 0) =>
        new(id, box, TimesSeen: 1, TimesCorrect: 0, TimesMissed: missed, LastSession: lastSession);

    private static IEnumerable<int> Unseen(int from, int count) => Enumerable.Range(from, count);

    [Fact]
    public void A_first_session_is_all_new_words()
    {
        var plan = _composer.Compose(sessionNumber: 1, size: 10, seen: [], unseenWordIds: Unseen(1, 500));

        Assert.Empty(plan.ReviewWordIds);
        Assert.Equal(10, plan.NewWordIds.Count);
        Assert.Equal(Enumerable.Range(1, 10), plan.NewWordIds);
    }

    [Fact]
    public void Words_answered_last_session_are_not_asked_again()
    {
        // The whole point: session 2 must not repeat session 1.
        var seen = Enumerable.Range(1, 10).Select(id => Seen(id, box: 1, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 2, size: 10, seen, Unseen(11, 500));

        Assert.Empty(plan.ReviewWordIds);
        Assert.DoesNotContain(plan.AllWordIds, id => id <= 10);
    }

    [Fact]
    public void New_words_are_taken_in_the_order_supplied()
    {
        // The source yields the most frequent words first, so easiest material comes first.
        var plan = _composer.Compose(1, 5, [], Unseen(100, 500));

        Assert.Equal([100, 101, 102, 103, 104], plan.NewWordIds);
    }

    [Fact]
    public void Due_words_come_back_alongside_new_ones()
    {
        var seen = new[]
        {
            Seen(1, box: 1, lastSession: 1),  // box 1 waits five sessions
            Seen(2, box: 1, lastSession: 1),
        };

        var plan = _composer.Compose(sessionNumber: 6, size: 10, seen, Unseen(50, 500));

        Assert.Equal([1, 2], plan.ReviewWordIds);
        Assert.Equal(8, plan.NewWordIds.Count);
        Assert.Equal(10, plan.Count);
    }

    [Fact]
    public void Revision_never_takes_more_than_half_a_session()
    {
        // Twenty overdue words must not crowd out all new material.
        var seen = Enumerable.Range(1, 20).Select(id => Seen(id, box: 0, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 10, size: 10, seen, Unseen(100, 500));

        Assert.Equal(5, plan.ReviewWordIds.Count);
        Assert.Equal(5, plan.NewWordIds.Count);
    }

    [Fact]
    public void The_longest_overdue_words_are_revised_first()
    {
        var seen = new[]
        {
            Seen(1, box: 0, lastSession: 8),
            Seen(2, box: 0, lastSession: 2),  // overdue the longest
            Seen(3, box: 0, lastSession: 5),
        };

        var plan = _composer.Compose(sessionNumber: 10, size: 4, seen, Unseen(100, 50));

        Assert.Equal([2, 3], plan.ReviewWordIds);
    }

    [Fact]
    public void Troublesome_words_win_a_tie()
    {
        var seen = new[]
        {
            Seen(1, box: 0, lastSession: 3, missed: 1),
            Seen(2, box: 0, lastSession: 3, missed: 9),
        };

        var plan = _composer.Compose(sessionNumber: 10, size: 2, seen, Unseen(100, 50));

        Assert.Equal([2], plan.ReviewWordIds);
    }

    [Fact]
    public void Mastered_words_are_never_scheduled()
    {
        var seen = Enumerable
            .Range(1, 10)
            .Select(id => Seen(id, box: ReviewSchedule.MasteredBox, lastSession: 1))
            .ToList();

        var plan = _composer.Compose(sessionNumber: 9_999, size: 10, seen, Unseen(50, 500));

        Assert.Empty(plan.ReviewWordIds);
    }

    [Fact]
    public void Revision_fills_the_session_once_the_dictionary_runs_out()
    {
        // With no new words left, the half-session cap would otherwise waste slots.
        var seen = Enumerable.Range(1, 20).Select(id => Seen(id, box: 0, lastSession: 1)).ToList();

        var plan = _composer.Compose(sessionNumber: 10, size: 10, seen, unseenWordIds: []);

        Assert.Equal(10, plan.ReviewWordIds.Count);
        Assert.Equal(10, plan.ReviewWordIds.Distinct().Count());
    }

    [Fact]
    public void A_short_session_is_returned_when_nothing_else_is_available()
    {
        var plan = _composer.Compose(sessionNumber: 5, size: 10, seen: [], unseenWordIds: Unseen(1, 3));

        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void An_exhausted_dictionary_with_nothing_due_yields_an_empty_plan()
    {
        var seen = new[] { Seen(1, box: 2, lastSession: 10) };

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
}
