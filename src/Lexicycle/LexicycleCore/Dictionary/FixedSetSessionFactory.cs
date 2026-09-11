using LexicycleCore.Models;
using LexicycleCore.Progress;
using LexicycleCore.Session;

namespace LexicycleCore.Dictionary;

/// <summary>
/// Builds a session from a fixed set — the bundled JSON, and later an OCR'd page —
/// applying the same selection policy as the dictionary-backed Practice session.
///
/// Fixed sets used to be drilled whole, every visit, which meant opening "German basics"
/// twice in a row asked the identical twelve questions. A set is a pool to draw from, not
/// a script to replay.
///
/// Words are identified by their position in the set, which is stable as long as the set
/// is, and scoped by set id so those positions cannot collide with dictionary word ids.
/// </summary>
public sealed class FixedSetSessionFactory
{
    /// <summary>Upper bound on a session, matching the dictionary's.</summary>
    public const int DefaultSize = 10;

    private readonly IProgressStore _progress;
    private readonly SessionComposer _composer;

    public FixedSetSessionFactory(IProgressStore progress, SessionComposer? composer = null)
    {
        _progress = progress;
        _composer = composer ?? new SessionComposer();
    }

    /// <summary>A session drawn from a fixed set, plus what is needed to record it.</summary>
    public sealed record FixedSetSession(string Scope, int SessionNumber, VocabularySet Set)
    {
        /// <summary>Position within the original set, keyed by prompt.</summary>
        public required IReadOnlyDictionary<string, int> IndexBySource { get; init; }

        public bool IsEmpty => Set.WordCount == 0;
    }

    /// <summary>
    /// How many words to ask from a set of <paramref name="wordCount"/>.
    ///
    /// Never more than half the set. A session that used the whole set could not avoid
    /// repeating it, and one that used most of it would leave too little for the next
    /// visit — twelve words asked ten at a time gives a follow-up session of two. Halving
    /// guarantees at least two disjoint sessions back to back, which is what makes a
    /// second visit feel different.
    /// </summary>
    public static int SizeFor(int wordCount)
        => Math.Max(1, Math.Min(DefaultSize, wordCount / 2));

    public async Task<FixedSetSession> CreateAsync(
        VocabularySet set,
        CancellationToken cancellationToken = default)
    {
        var scope = ProgressScope.ForSet(set.Id);

        var seen = await _progress.GetAllAsync(scope, cancellationToken).ConfigureAwait(false);
        var sessionNumber = await _progress
            .BeginSessionAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        var alreadySeen = seen.Select(progress => progress.WordId).ToHashSet();
        var unseen = Enumerable
            .Range(0, set.WordCount)
            .Where(index => !alreadySeen.Contains(index));

        var plan = _composer.Compose(sessionNumber, SizeFor(set.WordCount), seen, unseen);

        // A fixed set has no frequency data, so there is no "most useful first" order to
        // preserve; the draw order is as good as any and already varies per session.
        var words = plan.AllWordIds.Select(index => set.Words[index]).ToList();

        var session = new VocabularySet(
            set.Id,
            set.Name,
            set.SourceLanguage,
            set.TargetLanguage,
            words);

        var indexBySource = plan.AllWordIds
            .GroupBy(index => set.Words[index].Source)
            .ToDictionary(group => group.Key, group => group.First());

        return new FixedSetSession(scope, sessionNumber, session)
        {
            IndexBySource = indexBySource,
        };
    }

    /// <summary>Writes a finished session's results back to the progress store.</summary>
    public async Task RecordAsync(
        FixedSetSession session,
        SessionSummary summary,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<WordOutcome>(summary.HardestWords.Count);

        foreach (var score in summary.HardestWords)
        {
            if (!session.IndexBySource.TryGetValue(score.Word.Source, out var index))
            {
                continue;
            }

            outcomes.Add(new WordOutcome(index, score.MissCount == 0, score.MissCount));
        }

        await _progress
            .RecordAsync(session.Scope, session.SessionNumber, outcomes, cancellationToken)
            .ConfigureAwait(false);
    }
}
