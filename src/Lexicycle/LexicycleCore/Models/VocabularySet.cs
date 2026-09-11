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

    /// <summary>
    /// The same set with its words in a new random order.
    ///
    /// Sets are stored in a fixed order — file order for the bundled JSON, frequency
    /// order for a generated session — and asking them in that order makes every visit to
    /// a fixed set identical: "German basics" opened with <c>house dog cat car</c> every
    /// single time. A session is composed of words, not of a sequence, so the order is
    /// chosen when the session starts rather than baked into the set.
    /// </summary>
    public VocabularySet Shuffled(Random? random = null)
    {
        var rng = random ?? Random.Shared;
        var words = Words.ToArray();

        // Fisher-Yates.
        for (var i = words.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (words[i], words[j]) = (words[j], words[i]);
        }

        return new VocabularySet(Id, Name, SourceLanguage, TargetLanguage, words);
    }
}
