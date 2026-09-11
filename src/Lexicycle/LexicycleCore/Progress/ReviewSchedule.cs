namespace LexicycleCore.Progress;

/// <summary>
/// Decides when a word already practised should be asked again.
///
/// A plain "never repeat" rule would let hard words fade, so Lexicycle uses a Leitner
/// schedule: answering correctly promotes a word to the next box and pushes it further
/// into the future; missing it sends it back to the start to be relearned. Once a word
/// clears the final box it is considered mastered and stops coming back.
///
/// Delays are counted in sessions rather than days, so someone practising twice a week
/// gets the same sequence as someone practising twice a day.
/// </summary>
public static class ReviewSchedule
{
    /// <summary>
    /// Sessions to wait before re-asking, indexed by box. A word in box 1 returns five
    /// sessions later, box 2 twelve sessions later, and so on.
    ///
    /// The first interval is deliberately long. Variety is the point of generating
    /// sessions at all, so a word answered correctly should stay away for a while; it is
    /// box 0 — words still being learned, and words just missed — that returns at once.
    /// </summary>
    public static readonly IReadOnlyList<int> Delays = [5, 12, 30, 90];

    /// <summary>
    /// Sessions to wait before re-asking a word still being learned (box 0).
    ///
    /// Two, not one. At one, a learner who misses a few words each session was served
    /// those same words in the very next session, over and over — the schedule kept
    /// demoting them to box 0, and box 0 was due immediately. Waiting two sessions still
    /// brings a missed word back quickly, but never in the session straight afterwards.
    /// </summary>
    public const int RelearnDelay = 2;

    /// <summary>A word promoted past the last box is mastered and never returns.</summary>
    public static int MasteredBox => Delays.Count + 1;

    public static bool IsMastered(WordProgress progress) => progress.Box >= MasteredBox;

    /// <summary>
    /// The box a word moves to after a session. Correct promotes by one; a miss sends it
    /// back to box 0, because a word you got wrong needs relearning, not a longer wait.
    /// </summary>
    public static int NextBox(int currentBox, bool answeredCorrectly)
    {
        if (!answeredCorrectly)
        {
            return 0;
        }

        return Math.Min(currentBox + 1, MasteredBox);
    }

    /// <summary>
    /// Session number at which this word becomes eligible again. Box 0 words — still
    /// being learned — come back soonest, but not in the very next session.
    /// </summary>
    public static int DueAtSession(WordProgress progress)
    {
        if (progress.Box <= 0)
        {
            return progress.LastSession + RelearnDelay;
        }

        if (IsMastered(progress))
        {
            return int.MaxValue;
        }

        // Box 1 uses Delays[0], box 2 uses Delays[1], and so on.
        var delay = Delays[Math.Min(progress.Box, Delays.Count) - 1];
        return progress.LastSession + delay;
    }

    public static bool IsDue(WordProgress progress, int sessionNumber)
        => !IsMastered(progress) && sessionNumber >= DueAtSession(progress);
}
