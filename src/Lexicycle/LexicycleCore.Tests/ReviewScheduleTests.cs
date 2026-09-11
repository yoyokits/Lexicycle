using LexicycleCore.Progress;

namespace LexicycleCore.Tests;

/// <summary>
/// Boxes are a measure of how well a word is known. They do not decide what a session
/// asks — that is <see cref="SessionComposer"/>, covered by SessionComposerTests.
/// </summary>
public class ReviewScheduleTests
{
    private static WordProgress At(int box, int lastSession) =>
        new(WordId: 1, box, TimesSeen: 1, TimesCorrect: 0, TimesMissed: 0, LastSession: lastSession);

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
    [InlineData(4)]
    public void A_miss_sends_a_word_back_to_the_start(int box)
    {
        // Relearning, not a longer wait: a word you got wrong is not partly known.
        Assert.Equal(0, ReviewSchedule.NextBox(box, answeredCorrectly: false));
    }

    [Fact]
    public void Promotion_stops_at_mastered()
    {
        var box = ReviewSchedule.MasteredBox;

        Assert.Equal(ReviewSchedule.MasteredBox, ReviewSchedule.NextBox(box, answeredCorrectly: true));
    }

    [Fact]
    public void Mastery_takes_one_correct_answer_per_box()
    {
        var box = 0;
        for (var i = 0; i < ReviewSchedule.Boxes + 1; i++)
        {
            box = ReviewSchedule.NextBox(box, answeredCorrectly: true);
        }

        Assert.True(ReviewSchedule.IsMastered(At(box, lastSession: 1)));
    }

    [Fact]
    public void A_word_short_of_the_last_box_is_not_yet_mastered()
    {
        Assert.False(ReviewSchedule.IsMastered(At(ReviewSchedule.Boxes, lastSession: 1)));
    }

    [Fact]
    public void One_miss_undoes_every_promotion()
    {
        var box = ReviewSchedule.MasteredBox;

        box = ReviewSchedule.NextBox(box, answeredCorrectly: false);

        Assert.Equal(0, box);
        Assert.False(ReviewSchedule.IsMastered(At(box, lastSession: 1)));
    }
}
