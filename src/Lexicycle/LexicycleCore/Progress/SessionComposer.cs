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
/// The policy, in order of priority:
///
/// 1. <b>At least 80% new.</b> Most of every session is vocabulary the learner has never
///    been asked, taken most-common-first so useful words arrive before obscure ones.
/// 2. <b>The remainder is revision, weighted towards failures.</b> A word answered wrong
///    often is more likely to come back than one answered wrong once.
/// 3. <b>Weighted, not sorted.</b> Revision is drawn by weighted random sampling rather
///    than by taking the worst few, so the hardest word does not reappear every single
///    session and gradually crowd out everything else.
/// 4. <b>Never twice running.</b> Nothing asked in the immediately preceding session is
///    offered, whatever its failure count.
///
/// Sets that come from outside — the bundled JSON, and later an OCR'd page — bypass all of
/// this and drill exactly the words they were given.
/// </summary>
public sealed class SessionComposer
{
    /// <summary>
    /// The share of each session that must be words never asked before. Revision gets
    /// whatever is left, so a 10-word session is 8 new and at most 2 review.
    /// </summary>
    public const double MinNewShare = 0.8;

    /// <summary>
    /// Added to every candidate's wrong-answer count before sampling, so a word that has
    /// never been missed still has some chance of coming back and a word missed twice is
    /// three times as likely as one missed zero times — not infinitely more likely.
    /// </summary>
    private const int WeightFloor = 1;

    private readonly Random _random;

    public SessionComposer(Random? random = null) => _random = random ?? Random.Shared;

    /// <summary>Builds a plan of <paramref name="size"/> words.</summary>
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

        // The source may be a lazy query over the whole dictionary, so enumerate it once
        // and never past what a single session could possibly use.
        var available = unseenWordIds.Take(size).ToList();

        // Ceiling, so "at least 80%" holds at every size: 10 -> 8 new, 5 -> 4 new.
        var newTarget = (int)Math.Ceiling(size * MinNewShare);
        var fresh = available.Take(newTarget).ToList();

        // Revision fills what is left over, which grows if the dictionary runs dry.
        var candidates = seen
            .Where(progress => IsAvailable(progress, sessionNumber))
            .ToList();

        var review = SampleByFailure(candidates, size - fresh.Count);

        // If revision could not fill the gap either, take more new words rather than
        // leaving the session short.
        if (fresh.Count + review.Count < size)
        {
            fresh = [.. available.Take(size - review.Count)];
        }

        return new SessionPlan(review, fresh);
    }

    /// <summary>
    /// Draws up to <paramref name="count"/> words at random, with a word's chance
    /// proportional to how often it has been answered wrong.
    ///
    /// Sampling rather than sorting is the point. Taking the most-failed words outright
    /// would serve the same handful every session until they were finally learned, which
    /// is exactly the "it keeps asking me the same things" complaint; weighting makes
    /// them likely without making them certain.
    /// </summary>
    private List<int> SampleByFailure(List<WordProgress> candidates, int count)
    {
        var chosen = new List<int>(Math.Max(0, count));
        if (count <= 0 || candidates.Count == 0)
        {
            return chosen;
        }

        var pool = new List<WordProgress>(candidates);
        var weights = pool.Select(p => (double)p.TimesMissed + WeightFloor).ToList();
        var total = weights.Sum();

        while (chosen.Count < count && pool.Count > 0)
        {
            var target = _random.NextDouble() * total;

            var index = 0;
            var running = 0.0;
            for (; index < pool.Count - 1; index++)
            {
                running += weights[index];
                if (running > target)
                {
                    break;
                }
            }

            chosen.Add(pool[index].WordId);

            // Without replacement: a word cannot be drawn twice into one session.
            total -= weights[index];
            pool.RemoveAt(index);
            weights.RemoveAt(index);
        }

        return chosen;
    }

    /// <summary>
    /// Whether a word may be revised in this session. The only bar is that it was not
    /// asked in the session immediately before — seeing the same word twice running makes
    /// the app feel stuck, and it is the complaint that keeps coming back. Everything else
    /// is left to the failure weighting.
    /// </summary>
    private static bool IsAvailable(WordProgress progress, int sessionNumber)
        => progress.LastSession != sessionNumber - 1;
}
