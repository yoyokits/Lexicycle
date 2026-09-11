namespace LexicycleCore.Progress;

/// <summary>Which words a generated session will ask, split by where they came from.</summary>
public sealed record SessionPlan(IReadOnlyList<int> ReviewWordIds, IReadOnlyList<int> NewWordIds)
{
    public IReadOnlyList<int> AllWordIds => [.. ReviewWordIds, .. NewWordIds];

    public int Count => ReviewWordIds.Count + NewWordIds.Count;

    public static SessionPlan Empty { get; } = new([], []);
}

/// <summary>
/// Chooses the words for a generated session.
///
/// The point is that consecutive sessions ask different things: words already practised
/// are not served again until the <see cref="ReviewSchedule"/> says they are due, and the
/// rest of each session is filled with words never seen before. Sets that come from
/// outside — the bundled JSON, and later an OCR'd page — bypass this entirely and drill
/// exactly the words they were given.
///
/// On top of the schedule there is one absolute rule: nothing asked in the immediately
/// preceding session may appear. See <see cref="IsAvailable"/>.
/// </summary>
public sealed class SessionComposer
{
    /// <summary>At most this share of a session is revision, so new material keeps coming.</summary>
    public const double MaxReviewShare = 0.5;

    /// <summary>
    /// Builds a plan of <paramref name="size"/> words.
    /// </summary>
    /// <param name="sessionNumber">The session about to start, counting from 1.</param>
    /// <param name="seen">Progress for every word the learner has already been asked.</param>
    /// <param name="unseenWordIds">
    /// Candidate words never asked before, most useful first. Only as many as needed are
    /// taken, so this can be a lazily evaluated sequence over the whole dictionary.
    /// </param>
    public SessionPlan Compose(
        int sessionNumber,
        int size,
        IEnumerable<WordProgress> seen,
        IEnumerable<int> unseenWordIds)
    {
        if (size <= 0)
        {
            return SessionPlan.Empty;
        }

        var reviewBudget = (int)Math.Floor(size * MaxReviewShare);

        var due = seen
            .Where(progress => IsAvailable(progress, sessionNumber))
            // Longest overdue first, then the words that have caused the most trouble.
            .OrderBy(ReviewSchedule.DueAtSession)
            .ThenByDescending(progress => progress.TimesMissed)
            .ThenBy(progress => progress.WordId)
            .Take(reviewBudget)
            .Select(progress => progress.WordId)
            .ToList();

        var fresh = unseenWordIds.Take(size - due.Count).ToList();

        // A short session is better than a padded one, but if the dictionary is exhausted
        // there is no reason to leave review slots unused.
        if (fresh.Count + due.Count < size)
        {
            var alreadyChosen = due.ToHashSet();

            var extra = seen
                .Where(progress => IsAvailable(progress, sessionNumber))
                .Where(progress => !alreadyChosen.Contains(progress.WordId))
                .OrderBy(ReviewSchedule.DueAtSession)
                .ThenBy(progress => progress.WordId)
                .Take(size - fresh.Count - due.Count)
                .Select(progress => progress.WordId);

            due = [.. due, .. extra];
        }

        return new SessionPlan(due, fresh);
    }

    /// <summary>
    /// Whether a word may be asked in this session: due by the schedule, and not asked in
    /// the session immediately before.
    ///
    /// The second condition is a hard floor rather than a consequence of the delays. It is
    /// the rule a learner actually notices — seeing the same word twice running makes the
    /// app feel stuck — and keeping it here means retuning
    /// <see cref="ReviewSchedule.Delays"/> can never quietly reintroduce the problem. It
    /// applies to the exhausted-dictionary path too: a shorter session is better than one
    /// that repeats what was just asked.
    /// </summary>
    private static bool IsAvailable(WordProgress progress, int sessionNumber)
        => ReviewSchedule.IsDue(progress, sessionNumber)
           && progress.LastSession != sessionNumber - 1;
}
