using LexicycleCore.Models;
using LexicycleCore.Progress;
using LexicycleCore.Session;

namespace LexicycleCore.Dictionary;

/// <summary>
/// Builds a practice session from the dictionary, remembering what has been asked so the
/// next one is different.
///
/// This is the only path that consults progress. Sets that come from outside — the
/// bundled JSON, and later an OCR'd page — go straight to <see cref="SessionEngine"/>
/// with exactly the words they were given, because the learner chose those words and
/// rotating them away would be wrong.
/// </summary>
public sealed class PracticeSessionFactory
{
    public const string GeneratedSetId = "dictionary-practice";
    public const int DefaultSize = 10;

    private readonly IDictionaryStore _dictionary;
    private readonly IProgressStore _progress;
    private readonly SessionComposer _composer;

    public PracticeSessionFactory(
        IDictionaryStore dictionary,
        IProgressStore progress,
        SessionComposer? composer = null)
    {
        _dictionary = dictionary;
        _progress = progress;
        _composer = composer ?? new SessionComposer();
    }

    /// <summary>A generated session, plus the bookkeeping needed to record its results.</summary>
    public sealed record PracticeSession(
        int SessionNumber,
        VocabularySet Set,
        IReadOnlyDictionary<string, int> WordIdsBySource)
    {
        public bool IsEmpty => Set.WordCount == 0;
    }

    /// <summary>
    /// Starts a session. This increments the session counter even if the caller abandons
    /// the session, which is deliberate: the counter paces the review schedule, and an
    /// abandoned session should still push revision forward rather than stall it.
    /// </summary>
    /// <param name="band">
    /// Restricts the draw to one frequency band. Progress stays in the single dictionary
    /// scope whichever band is used, so a word learned under "Basics" is not asked again
    /// under "Practice" — the bands are views over one body of vocabulary, not separate
    /// courses.
    /// </param>
    public async Task<PracticeSession> CreateAsync(
        int size = DefaultSize,
        FrequencyBand? band = null,
        CancellationToken cancellationToken = default)
    {
        var seen = await _progress
            .GetAllAsync(ProgressScope.Dictionary, cancellationToken)
            .ConfigureAwait(false);
        var sessionNumber = await _progress
            .BeginSessionAsync(ProgressScope.Dictionary, cancellationToken)
            .ConfigureAwait(false);

        // Only words never asked before are candidates for the "new" half.
        var alreadySeen = seen.Select(progress => progress.WordId).ToHashSet();
        var unseen = await _dictionary
            .GetUnseenIdsAsync(alreadySeen, size, band, cancellationToken)
            .ConfigureAwait(false);

        var plan = _composer.Compose(sessionNumber, size, seen, unseen);
        var words = await _dictionary
            .GetWordsAsync(plan.AllWordIds, cancellationToken)
            .ConfigureAwait(false);

        // Most common first, so the words worth knowing come before the obscure ones.
        // Unranked words sort last; they are the rare tail of the dictionary.
        var ordered = words
            .OrderBy(word => word.FreqRank ?? int.MaxValue)
            .ThenBy(word => word.Source, StringComparer.Ordinal)
            .ToList();

        var set = new VocabularySet(
            band?.Id ?? GeneratedSetId,
            band is null ? $"Practice · session {sessionNumber}" : band.Name,
            "en",
            "de",
            ordered.Select(word => word.ToWordPair()).ToList());

        // The engine works in WordPairs; this maps its summary back to dictionary ids.
        var idsBySource = ordered
            .GroupBy(word => word.Source)
            .ToDictionary(group => group.Key, group => group.First().Id);

        return new PracticeSession(sessionNumber, set, idsBySource);
    }

    /// <summary>Writes a finished session's results back to the progress store.</summary>
    public async Task RecordAsync(
        PracticeSession session,
        SessionSummary summary,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<WordOutcome>(summary.HardestWords.Count);

        foreach (var score in summary.HardestWords)
        {
            if (!session.WordIdsBySource.TryGetValue(score.Word.Source, out var wordId))
            {
                continue;
            }

            // Reaching the summary means every word was answered correctly at some point;
            // the miss count is what decides whether it was learned or merely survived.
            outcomes.Add(new WordOutcome(wordId, score.MissCount == 0, score.MissCount));
        }

        await _progress
            .RecordAsync(ProgressScope.Dictionary, session.SessionNumber, outcomes, cancellationToken)
            .ConfigureAwait(false);
    }
}
