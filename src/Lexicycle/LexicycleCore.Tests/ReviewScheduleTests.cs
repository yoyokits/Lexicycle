using LexicycleCore.Progress;

namespace LexicycleCore.Tests;

public class ReviewScheduleTests
{
    private static WordProgress At(int box, int lastSession, int missed = 0) =>
        new(WordId: 1, Box: box, TimesSeen: 1, TimesCorrect: 0, TimesMissed: missed, LastSession: lastSession);

    // --- promotion and demotion -----------------------------------------------------------

    [Fact]
    public void A_correct_answer_promotes_one_box()
    {
        Assert.Equal(1, ReviewSchedule.NextBox(0, answeredCorrectly: true));
        Assert.Equal(2, ReviewSchedule.NextBox(1, answeredCorrectly: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_miss_sends_the_word_back_to_the_start(int box)
    {
        // A word you got wrong needs relearning, not a longer wait.
        Assert.Equal(0, ReviewSchedule.NextBox(box, answeredCorrectly: false));
    }

    [Fact]
    public void Promotion_stops_at_mastered()
    {
        var box = ReviewSchedule.MasteredBox;

        Assert.Equal(ReviewSchedule.MasteredBox, ReviewSchedule.NextBox(box, answeredCorrectly: true));
    }

    // --- when a word comes back -----------------------------------------------------------

    [Fact]
    public void A_word_still_being_learned_is_due_in_the_next_session()
    {
        Assert.True(ReviewSchedule.IsDue(At(box: 0, lastSession: 4), sessionNumber: 5));
    }

    [Fact]
    public void The_wait_grows_with_each_box()
    {
        // Delays are 5, 12, 30, 90 sessions.
        Assert.Equal(15, ReviewSchedule.DueAtSession(At(box: 1, lastSession: 10)));
        Assert.Equal(22, ReviewSchedule.DueAtSession(At(box: 2, lastSession: 10)));
        Assert.Equal(40, ReviewSchedule.DueAtSession(At(box: 3, lastSession: 10)));
        Assert.Equal(100, ReviewSchedule.DueAtSession(At(box: 4, lastSession: 10)));
    }

    [Fact]
    public void A_word_answered_correctly_is_not_asked_again_next_session()
    {
        // This is the behaviour the feature exists for: no immediate repeats.
        // Box 1 waits five sessions, so a word answered in session 7 returns in session 12.
        var justAnswered = At(box: 1, lastSession: 7);

        Assert.False(ReviewSchedule.IsDue(justAnswered, sessionNumber: 8));
        Assert.False(ReviewSchedule.IsDue(justAnswered, sessionNumber: 11));
        Assert.True(ReviewSchedule.IsDue(justAnswered, sessionNumber: 12));
    }

    [Fact]
    public void A_mastered_word_never_returns()
    {
        var mastered = At(box: ReviewSchedule.MasteredBox, lastSession: 1);

        Assert.True(ReviewSchedule.IsMastered(mastered));
        Assert.False(ReviewSchedule.IsDue(mastered, sessionNumber: 10_000));
    }

    [Fact]
    public void A_missed_word_returns_soon_rather_than_being_dropped()
    {
        var relearning = At(box: ReviewSchedule.NextBox(3, answeredCorrectly: false), lastSession: 9);

        Assert.True(ReviewSchedule.IsDue(relearning, sessionNumber: 10));
    }

    [Fact]
    public void Mastery_takes_one_correct_answer_per_box()
    {
        var box = 0;
        for (var i = 0; i < ReviewSchedule.Delays.Count + 1; i++)
        {
            box = ReviewSchedule.NextBox(box, answeredCorrectly: true);
        }

        Assert.True(ReviewSchedule.IsMastered(At(box, lastSession: 1)));
    }
}
