using LexicycleCore.Session;

namespace LexicycleApp.Services;

/// <summary>User preferences, persisted across launches.</summary>
public sealed class AppSettings
{
    private const string LenientDiacriticsKey = "settings.lenient_diacritics";
    private const string SelectedPairKey = "settings.selected_pair";

    /// <summary>
    /// When on, "Madchen" and "Maedchen" both count as "Mädchen". Default on — typing
    /// umlauts and accents on a phone keyboard is more friction than it is worth.
    /// </summary>
    public bool LenientDiacritics
    {
        get => Preferences.Default.Get(LenientDiacriticsKey, true);
        set => Preferences.Default.Set(LenientDiacriticsKey, value);
    }

    /// <summary>The language pair id the learner last practised, so the home screen
    /// reopens on it rather than always defaulting back to German.</summary>
    public string? SelectedPairId
    {
        get => Preferences.Default.Get(SelectedPairKey, (string?)null);
        set => Preferences.Default.Set(SelectedPairKey, value);
    }

    /// <summary>A comparer configured from the current preferences.</summary>
    public AnswerComparer CreateComparer() => new(LenientDiacritics);
}
