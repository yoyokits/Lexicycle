using LexicycleCore.Models;

namespace LexicycleCore.Session;

/// <summary>What happened when the user submitted one answer.</summary>
public sealed record AnswerResult(
    WordPair Word,
    bool IsCorrect,
    string CorrectAnswer,
    bool RoundCompleted,
    bool SessionCompleted);

/// <summary>How often one word was missed across the whole session.</summary>
public sealed record WordScore(WordPair Word, int MissCount);

/// <summary>End-of-session statistics.</summary>
public sealed record SessionSummary(
    int RoundsTaken,
    int TotalWords,
    IReadOnlyList<WordScore> HardestWords)
{
    /// <summary>Words the user missed at least once. Ordered hardest first.</summary>
    public IReadOnlyList<WordScore> MissedWords { get; } =
        HardestWords.Where(score => score.MissCount > 0).ToList();

    /// <summary>True when every word was answered correctly on the very first attempt.</summary>
    public bool WasPerfect => MissedWords.Count == 0;
}
