namespace LexicycleCore.Models;

/// <summary>
/// A named list of word pairs for one language direction.
/// </summary>
public sealed class VocabularySet
{
    public VocabularySet(
        string id,
        string name,
        string sourceLanguage,
        string targetLanguage,
        IReadOnlyList<WordPair> words)
    {
        Id = id;
        Name = name;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        Words = words ?? throw new ArgumentNullException(nameof(words));
    }

    /// <summary>Stable identifier, e.g. "en-de-basics". Used for navigation and lookup.</summary>
    public string Id { get; }

    /// <summary>Display name, e.g. "English → German basics".</summary>
    public string Name { get; }

    /// <summary>BCP-47-ish language code of the prompt side, e.g. "en".</summary>
    public string SourceLanguage { get; }

    /// <summary>BCP-47-ish language code of the answer side, e.g. "de".</summary>
    public string TargetLanguage { get; }

    public IReadOnlyList<WordPair> Words { get; }

    public int WordCount => Words.Count;

    /// <summary>Display form of the direction, e.g. "en → de".</summary>
    public string LanguagePair => $"{SourceLanguage} → {TargetLanguage}";
}
