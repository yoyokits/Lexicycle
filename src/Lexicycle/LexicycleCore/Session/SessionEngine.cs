using LexicycleCore.Models;

namespace LexicycleCore.Session;

/// <summary>
/// Drives one practice session, independent of any UI.
///
/// A session is a series of rounds. Every word in the current round is asked exactly
/// once; a correct answer retires the word, a miss defers it to the next round (it is
/// never re-asked immediately). The session ends when a full round completes with zero
/// misses — so every word has been answered correctly at least once.
/// </summary>
public sealed class SessionEngine
{
    private readonly AnswerComparer _comparer;
    private readonly Dictionary<WordPair, int> _missCounts = new();

    private List<WordPair> _currentRound;
    private List<WordPair> _missedThisRound = new();
    private int _index;

    public SessionEngine(VocabularySet set, AnswerComparer? comparer = null)
    {
        Set = set ?? throw new ArgumentNullException(nameof(set));
        _comparer = comparer ?? new AnswerComparer();
        _currentRound = set.Words.ToList();

        foreach (var word in _currentRound)
        {
            _missCounts[word] = 0;
        }

        RoundNumber = 1;

        // An empty set has nothing to ask, so it is finished before it starts.
        IsComplete = _currentRound.Count == 0;
    }

    public VocabularySet Set { get; }

    /// <summary>1-based index of the round in progress.</summary>
    public int RoundNumber { get; private set; }

    /// <summary>How many words this round asks.</summary>
    public int WordsInRound => _currentRound.Count;

    /// <summary>1-based position of the current word within the round, for "Word 3 of 10".</summary>
    public int PositionInRound => Math.Min(_index + 1, _currentRound.Count);

    /// <summary>The word being asked, or null once the session is complete.</summary>
    public WordPair? CurrentWord =>
        IsComplete || _index >= _currentRound.Count ? null : _currentRound[_index];

    public bool IsComplete { get; private set; }

    /// <summary>
    /// Grades <paramref name="typed"/> against the current word and advances.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session is already complete.</exception>
    public AnswerResult Submit(string? typed)
    {
        var word = CurrentWord
            ?? throw new InvalidOperationException("The session is complete; there is nothing to answer.");

        var isCorrect = _comparer.IsCorrect(word, typed);

        if (!isCorrect)
        {
            _missCounts[word]++;
            _missedThisRound.Add(word);
        }

        _index++;

        var roundCompleted = _index >= _currentRound.Count;
        if (roundCompleted)
        {
            AdvanceRound();
        }

        return new AnswerResult(
            word,
            isCorrect,
            word.PrimaryAnswer,
            roundCompleted,
            IsComplete);
    }

    /// <summary>
    /// Closes the round: a clean round ends the session, otherwise the missed words
    /// become the next round.
    /// </summary>
    private void AdvanceRound()
    {
        if (_missedThisRound.Count == 0)
        {
            IsComplete = true;
            return;
        }

        _currentRound = _missedThisRound;
        _missedThisRound = new List<WordPair>();
        _index = 0;
        RoundNumber++;
    }

    /// <summary>
    /// Statistics for the summary screen. Safe to call mid-session, though the round
    /// count then reflects progress so far rather than a finished session.
    /// </summary>
    public SessionSummary BuildSummary()
    {
        var scores = _missCounts
            .Select(entry => new WordScore(entry.Key, entry.Value))
            .OrderByDescending(score => score.MissCount)
            .ThenBy(score => score.Word.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new SessionSummary(RoundNumber, Set.WordCount, scores);
    }
}
