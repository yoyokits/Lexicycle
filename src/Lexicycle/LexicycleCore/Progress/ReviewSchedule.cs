namespace LexicycleCore.Progress;

/// <summary>
/// Tracks how well a word is known, as a Leitner-style box number.
///
/// This is a <i>statistic</i>, not a schedule. Which words a session asks is decided by
/// <see cref="SessionComposer"/> — mostly new material, with a small failure-weighted
/// slice of revision — and nothing here gates that choice.
///
/// An earlier version did drive selection, through fixed per-box intervals ("box 1
/// returns in five sessions"). It was removed rather than left alongside the current
/// policy: two scheduling models in the same codebase, only one of them live, is a trap
/// for whoever reads it next. Git history has it if the interval approach is ever wanted
/// back.
/// </summary>
public static class ReviewSchedule
{
    /// <summary>Boxes above the last are mastered. Four steps to get there.</summary>
    public const int Boxes = 4;

    /// <summary>A word promoted past the last box is considered mastered.</summary>
    public static int MasteredBox => Boxes + 1;

    public static bool IsMastered(WordProgress progress) => progress.Box >= MasteredBox;

    /// <summary>
    /// The box a word moves to after a session. Correct promotes by one; a miss sends it
    /// back to box 0, because a word you got wrong needs relearning from the start.
    /// </summary>
    public static int NextBox(int currentBox, bool answeredCorrectly)
    {
        if (!answeredCorrectly)
        {
            return 0;
        }

        return Math.Min(currentBox + 1, MasteredBox);
    }
}
