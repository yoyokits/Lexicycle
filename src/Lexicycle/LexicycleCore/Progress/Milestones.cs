namespace LexicycleCore.Progress;

/// <summary>
/// How far the learner is towards the next milestone. <paramref name="Learned"/> counts
/// distinct words answered correctly at least once, and <paramref name="Target"/> is the
/// milestone currently being worked towards.
/// </summary>
public sealed record MilestoneProgress(int Learned, int Target)
{
    /// <summary>0–1, for a progress bar.</summary>
    public double Fraction => Target <= 0 ? 0 : Math.Clamp((double)Learned / Target, 0, 1);

    public int Remaining => Math.Max(0, Target - Learned);
}

/// <summary>
/// The ladder of "words learned" milestones: 10, 50, 100, 500, 1000, then every further
/// thousand. Close together at the start, where a beginner needs to see movement, and
/// widening once progress is steady.
/// </summary>
public static class Milestones
{
    /// <summary>The hand-picked early rungs. Beyond the last one the ladder is regular.</summary>
    private static readonly int[] Early = [10, 50, 100, 500, 1000];

    /// <summary>Spacing once <see cref="Early"/> is exhausted.</summary>
    private const int Step = 1000;

    /// <summary>
    /// The milestone being worked towards — the lowest one strictly above
    /// <paramref name="learned"/>. Landing exactly on a milestone moves the goal to the
    /// next rung, so the bar never sits full.
    /// </summary>
    public static int Next(int learned)
    {
        foreach (var rung in Early)
        {
            if (learned < rung)
            {
                return rung;
            }
        }

        return (learned / Step + 1) * Step;
    }

    /// <summary>
    /// The highest milestone already reached, or <c>null</c> before the first one.
    /// </summary>
    public static int? HighestReached(int learned)
    {
        if (learned >= Step)
        {
            return learned / Step * Step;
        }

        int? reached = null;
        foreach (var rung in Early)
        {
            if (rung > learned)
            {
                break;
            }

            reached = rung;
        }

        return reached;
    }

    /// <summary>
    /// The milestone to celebrate after a count moved from <paramref name="before"/> to
    /// <paramref name="after"/>, or <c>null</c> if none was passed.
    ///
    /// A session that vaults several rungs at once reports only the highest, because two
    /// congratulations in a row for the same session would cheapen both.
    /// </summary>
    public static int? Crossed(int before, int after)
    {
        if (after <= before)
        {
            return null;
        }

        var reached = HighestReached(after);
        return reached > before ? reached : null;
    }

    /// <summary>Current standing, ready to bind to a progress bar.</summary>
    public static MilestoneProgress Describe(int learned) => new(learned, Next(learned));
}
