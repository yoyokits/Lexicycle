using LexicycleCore.Session;

namespace LexicycleApp.Services;

/// <summary>User preferences, persisted across launches.</summary>
public sealed class AppSettings
{
    private const string LenientDiacriticsKey = "settings.lenient_diacritics";

    /// <summary>
    /// When on, "Madchen" and "Maedchen" both count as "Mädchen". Default on — typing
    /// umlauts and accents on a phone keyboard is more friction than it is worth.
    /// </summary>
    public bool LenientDiacritics
    {
        get => Preferences.Default.Get(LenientDiacriticsKey, true);
        set => Preferences.Default.Set(LenientDiacriticsKey, value);
    }

    /// <summary>A comparer configured from the current preferences.</summary>
    public AnswerComparer CreateComparer() => new(LenientDiacritics);
}
