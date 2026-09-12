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
    /// Every answer treated as correct, e.g. ["Auto", "Wagen"] for "car", most common
    /// first — a generated pair carries every sense's translation (R-511), so "like" is
    /// gern, gern haben, gefallen and mögen.
    /// </summary>
    public IReadOnlyList<string> AcceptableAnswers { get; }

    /// <summary>Optional note shown alongside the prompt, e.g. a gender or usage hint.</summary>
    public string? Hint { get; }

    /// <summary>The single best answer — the most common one.</summary>
    public string PrimaryAnswer => AcceptableAnswers[0];

    /// <summary>
    /// Every accepted answer, most common first, for showing after a miss.
    ///
    /// Revealing only <see cref="PrimaryAnswer"/> taught the learner that one word was
    /// "the" translation, when several were accepted and any of them would have been
    /// marked correct — actively misleading once a prompt carries more than one sense.
    /// </summary>
    public string AllAnswers => string.Join(", ", AcceptableAnswers);
}
