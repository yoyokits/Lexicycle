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
    public const int DefaultSize = 10;

    /// <summary>Prefix of a whole-dictionary Practice session's route id, before the
    /// pair id — <c>"practice:en-de"</c>, <c>"practice:en-de:reverse"</c>.</summary>
    private const string GeneratedSetPrefix = "practice";

    private readonly LanguagePair _pair;
    private readonly bool _reversed;
    private readonly IDictionaryStore _dictionary;
    private readonly IProgressStore _progress;
    private readonly SessionComposer _composer;

    /// <param name="reversed">
    /// R-305: practise the other direction of this pair — the prompt is the
    /// target-language word, the answer is English. A completely separate progress scope
    /// and id space from the forward direction; see <see cref="ProgressScope.ForDictionary"/>.
    /// </param>
    public PracticeSessionFactory(
        LanguagePair pair,
        IDictionaryStore dictionary,
        IProgressStore progress,
        bool reversed = false,
        SessionComposer? composer = null)
    {
        _pair = pair;
        _reversed = reversed;
        _dictionary = dictionary;
        _progress = progress;
        _composer = composer ?? new SessionComposer();
    }

    /// <summary>The route id for practising the whole of one pair's dictionary, in the
    /// given direction.</summary>
    public static string GeneratedSetIdFor(LanguagePair pair, bool reversed = false)
        => reversed ? $"{GeneratedSetPrefix}:{pair.Id}:reverse" : $"{GeneratedSetPrefix}:{pair.Id}";

    /// <summary>What a generated-practice route id names: the pair and direction.</summary>
    public readonly record struct GeneratedRoute(LanguagePair Pair, bool Reversed);

    /// <summary>
    /// The pair and direction a generated-practice route id names, or null if it names
    /// something else (a frequency band, a bundled set) instead.
    /// </summary>
    public static GeneratedRoute? PairForGeneratedSetId(string setId)
    {
        var parts = setId.Split(':');
        if (parts.Length < 2 || parts[0] != GeneratedSetPrefix)
        {
            return null;
        }

        var pair = LanguagePair.ById(parts[1]);
        if (pair is null)
        {
            return null;
        }

        var reversed = parts.Length >= 3 && parts[2] == "reverse";
        return new GeneratedRoute(pair, reversed);
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
    /// courses. Must match this factory's own direction if given.
    /// </param>
    public async Task<PracticeSession> CreateAsync(
        int size = DefaultSize,
        FrequencyBand? band = null,
        CancellationToken cancellationToken = default)
    {
        var scope = ProgressScope.ForDictionary(_pair.Id, _reversed);
        var seen = await _progress
            .GetAllAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        var sessionNumber = await _progress
            .BeginSessionAsync(scope, cancellationToken)
            .ConfigureAwait(false);

        // Only words never asked before are candidates for the "new" half.
        var alreadySeen = seen.Select(progress => progress.WordId).ToHashSet();
        var unseen = await _dictionary
            .GetUnseenIdsAsync(alreadySeen, size, band, _reversed, cancellationToken)
            .ConfigureAwait(false);

        var plan = _composer.Compose(sessionNumber, size, seen, unseen);
        var words = await _dictionary
            .GetWordsAsync(plan.AllWordIds, _reversed, cancellationToken)
            .ConfigureAwait(false);

        // Most common first, so the words worth knowing come before the obscure ones.
        // Unranked words sort last; they are the rare tail of the dictionary.
        var ordered = words
            .OrderBy(word => word.FreqRank ?? int.MaxValue)
            .ThenBy(word => word.Source, StringComparer.Ordinal)
            .ToList();

        var name = band is null
            ? $"Practice · session {sessionNumber}"
            : band.Name;
        var set = new VocabularySet(
            band?.Id ?? GeneratedSetIdFor(_pair, _reversed),
            name,
            _reversed ? _pair.TargetLanguage : "en",
            _reversed ? "en" : _pair.TargetLanguage,
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
            .RecordAsync(
                ProgressScope.ForDictionary(_pair.Id, _reversed), session.SessionNumber, outcomes, cancellationToken)
            .ConfigureAwait(false);
    }
}
