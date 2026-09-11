using LexicycleCore.Progress;

namespace LexicycleCore.Tests;

public sealed class MilestonesTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 50)]      // landing on a rung moves the goal to the next one
    [InlineData(49, 50)]
    [InlineData(50, 100)]
    [InlineData(100, 500)]
    [InlineData(500, 1000)]
    [InlineData(999, 1000)]
    [InlineData(1000, 2000)]  // regular thousands from here on
    [InlineData(1001, 2000)]
    [InlineData(2000, 3000)]
    [InlineData(7321, 8000)]
    public void The_target_is_the_next_rung_above_the_count(int learned, int expected)
        => Assert.Equal(expected, Milestones.Next(learned));

    [Theory]
    [InlineData(0, null)]
    [InlineData(9, null)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(499, 100)]
    [InlineData(1000, 1000)]
    [InlineData(1999, 1000)]
    [InlineData(4000, 4000)]
    public void The_highest_rung_reached_is_reported(int learned, int? expected)
        => Assert.Equal(expected, Milestones.HighestReached(learned));

    [Fact]
    public void Crossing_a_rung_is_celebrated()
        => Assert.Equal(10, Milestones.Crossed(before: 8, after: 12));

    [Fact]
    public void Landing_exactly_on_a_rung_counts_as_crossing_it()
        => Assert.Equal(50, Milestones.Crossed(before: 47, after: 50));

    [Fact]
    public void Progress_within_a_band_celebrates_nothing()
        => Assert.Null(Milestones.Crossed(before: 12, after: 19));

    [Fact]
    public void A_rung_is_never_celebrated_twice()
    {
        // The learner passed 10 in an earlier session; sitting above it is not news.
        Assert.Null(Milestones.Crossed(before: 10, after: 10));
        Assert.Null(Milestones.Crossed(before: 15, after: 20));
    }

    [Fact]
    public void Vaulting_several_rungs_reports_only_the_highest()
    {
        // One congratulation, for the biggest number reached.
        Assert.Equal(100, Milestones.Crossed(before: 8, after: 140));
        Assert.Equal(3000, Milestones.Crossed(before: 900, after: 3200));
    }

    [Fact]
    public void A_count_that_did_not_move_celebrates_nothing()
        => Assert.Null(Milestones.Crossed(before: 10, after: 10));

    [Fact]
    public void The_bar_fills_towards_the_target_and_never_overflows()
    {
        var early = Milestones.Describe(0);
        Assert.Equal(10, early.Target);
        Assert.Equal(0, early.Fraction);
        Assert.Equal(10, early.Remaining);

        var midway = Milestones.Describe(25);
        Assert.Equal(50, midway.Target);
        Assert.Equal(0.5, midway.Fraction, precision: 6);
        Assert.Equal(25, midway.Remaining);

        // Target is always above Learned, so the bar cannot read as finished.
        var onRung = Milestones.Describe(1000);
        Assert.Equal(2000, onRung.Target);
        Assert.Equal(0.5, onRung.Fraction, precision: 6);
    }
}
