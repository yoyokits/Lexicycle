namespace LexicycleCore.Dictionary;

/// <summary>
/// One generated dictionary: English to some target language. Each pair is its own
/// bundled database (<c>lexicycle-dict-en-&lt;code&gt;.db</c>) and its own progress
/// scope, so learning German never counts as progress in Spanish.
/// </summary>
/// <param name="Id">The pair id used everywhere a pair needs naming — routes, asset
/// file names, progress scopes. Matches the Python pipeline's <c>--pair</c> value.</param>
/// <param name="TargetLanguage">ISO code of the target language, e.g. <c>"de"</c>.</param>
/// <param name="Name">Display name shown on the home screen's language switcher.</param>
public sealed record LanguagePair(string Id, string TargetLanguage, string Name)
{
    public static readonly LanguagePair German = new("en-de", "de", "German");
    public static readonly LanguagePair Spanish = new("en-es", "es", "Spanish");

    /// <summary>
    /// Every pair the app knows how to offer. A pair whose bundled database is missing
    /// (the dictionary has not been generated for it yet) is skipped at load time rather
    /// than removed here — see <c>AppDatabases</c>.
    /// </summary>
    public static IReadOnlyList<LanguagePair> All { get; } = [German, Spanish];

    public static LanguagePair? ById(string id) => All.FirstOrDefault(pair => pair.Id == id);

    /// <summary>Short label for a session card, e.g. "en → de" or, reversed, "de → en"
    /// (R-305: practising the other direction of the same pair).</summary>
    public string DirectionLabel(bool reversed = false)
        => reversed ? $"{TargetLanguage} → en" : $"en → {TargetLanguage}";
}
