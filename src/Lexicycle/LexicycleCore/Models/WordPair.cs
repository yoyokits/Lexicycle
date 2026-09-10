namespace LexicycleCore.Models;

/// <summary>
/// One vocabulary item: a source term and every translation that counts as correct.
/// </summary>
public sealed class WordPair
{
    public WordPair(string source, IReadOnlyList<string> acceptableAnswers, string? hint = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source term must not be empty.", nameof(source));
        }

        if (acceptableAnswers is null || acceptableAnswers.Count == 0)
        {
            throw new ArgumentException("A word pair needs at least one acceptable answer.", nameof(acceptableAnswers));
        }

        Source = source;
        AcceptableAnswers = acceptableAnswers;
        Hint = hint;
    }

    /// <summary>The prompt shown to the user, e.g. "house".</summary>
    public string Source { get; }

    /// <summary>
    /// Every answer treated as correct, e.g. ["Auto", "Wagen"] for "car".
    /// The first entry is the one shown as "the" answer on a miss.
    /// </summary>
    public IReadOnlyList<string> AcceptableAnswers { get; }

    /// <summary>Optional note shown alongside the prompt, e.g. a gender or usage hint.</summary>
    public string? Hint { get; }

    /// <summary>The answer displayed when the user gets it wrong.</summary>
    public string PrimaryAnswer => AcceptableAnswers[0];
}
