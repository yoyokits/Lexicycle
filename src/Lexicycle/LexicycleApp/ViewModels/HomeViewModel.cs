using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexicycleApp.Services;
using LexicycleCore.Dictionary;
using LexicycleCore.Models;
using LexicycleCore.Progress;
using LexicycleCore.Services;

namespace LexicycleApp.ViewModels;

/// <summary>Lists the available vocabulary sets and starts a session.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly AppDatabases _databases;

    /// <summary>Bands already fetched for a (pair, direction) combination, so switching
    /// back and forth does not re-query the dictionary — band membership does not change
    /// between visits.</summary>
    private readonly Dictionary<(string PairId, bool Reversed), List<BandRow>> _bandCache = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _practiceSubtitle = "Draws new words each time";

    [ObservableProperty]
    private string _milestoneHeadline = string.Empty;

    [ObservableProperty]
    private string _milestoneCaption = string.Empty;

    [ObservableProperty]
    private double _milestoneFraction;

    /// <summary>Hidden until progress can actually be read, so a failure shows nothing
    /// rather than a misleading empty bar.</summary>
    [ObservableProperty]
    private bool _hasMilestone;

    /// <summary>
    /// The pair currently being practised. Null only before the first load, or when no
    /// pair's dictionary could be opened at all.
    /// </summary>
    [ObservableProperty]
    private LanguagePair? _selectedPair;

    /// <summary>
    /// R-305: whether Practice and the bands currently ask the reversed direction —
    /// target-language word in, English answer out.
    /// </summary>
    [ObservableProperty]
    private bool _isReversed;

    public HomeViewModel(AppSettings settings, AppDatabases databases)
    {
        _settings = settings;
        _databases = databases;
        _isReversed = settings.PracticeReversed;
    }

    /// <summary>One frequency band on the home screen.</summary>
    public sealed record BandRow(FrequencyBand Band, string Subtitle)
    {
        public string Name => Band.Name;
    }

    public ObservableCollection<BandRow> Bands { get; } = [];

    /// <summary>
    /// Pairs whose bundled dictionary actually opened. A pair the pipeline has not been
    /// run for yet appears here rather than offering a language that immediately errors.
    /// </summary>
    public ObservableCollection<LanguagePair> AvailablePairs { get; } = [];

    /// <summary>The switcher only earns its place on screen once there is a choice.</summary>
    public bool HasLanguageChoice => AvailablePairs.Count > 1;

    /// <summary>"en → de" or, reversed, "de → en" — shown beside Practice and every band,
    /// and doubles as the direction toggle's own label.</summary>
    public string DirectionLabel => SelectedPair?.DirectionLabel(IsReversed) ?? string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedPairChanged(LanguagePair? value) => OnPropertyChanged(nameof(DirectionLabel));

    partial void OnIsReversedChanged(bool value) => OnPropertyChanged(nameof(DirectionLabel));

    /// <summary>Bound to the settings switch; writes straight through to preferences.</summary>
    public bool LenientDiacritics
    {
        get => _settings.LenientDiacritics;
        set
        {
            if (_settings.LenientDiacritics == value)
            {
                return;
            }

            _settings.LenientDiacritics = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Runs on every visit, not just the first. The sets themselves are fixed, but the
    /// learner has usually just finished a session, so the milestone bar has to be re-read.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await DiscoverPairsAsync();
            await RefreshProgressAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load vocabulary sets: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Finds which language pairs actually have a bundled dictionary, once. Cheap to
    /// call repeatedly — after the first successful discovery every pair is already
    /// cached by <see cref="AppDatabases"/>, so this just re-checks an in-memory list.
    /// </summary>
    private async Task DiscoverPairsAsync()
    {
        if (AvailablePairs.Count > 0)
        {
            return;
        }

        foreach (var pair in LanguagePair.All)
        {
            if (await _databases.GetDictionaryAsync(pair) is not null)
            {
                AvailablePairs.Add(pair);
            }
        }

        OnPropertyChanged(nameof(HasLanguageChoice));

        SelectedPair =
            AvailablePairs.FirstOrDefault(pair => pair.Id == _settings.SelectedPairId)
            ?? AvailablePairs.FirstOrDefault();
    }

    /// <summary>Shows how far through the dictionary the learner is, and towards the
    /// next milestone, for whichever pair and direction are currently selected.</summary>
    private async Task RefreshProgressAsync()
    {
        if (SelectedPair is not { } pair)
        {
            ErrorMessage = "No dictionary is bundled with this build.";
            PracticeSubtitle = "Unavailable";
            HasMilestone = false;
            return;
        }

        try
        {
            var dictionary = await _databases.GetDictionaryAsync(pair);
            if (dictionary is null)
            {
                // Discovered a moment ago; should not fail here, but the option must
                // vanish gracefully rather than crash if it somehow does.
                ErrorMessage = $"Could not open the {pair.Name} dictionary.";
                return;
            }

            var scope = ProgressScope.ForDictionary(pair.Id, IsReversed);
            var total = await dictionary.CountAsync(IsReversed);
            var seen = (await _databases.Progress.GetAllAsync(scope)).Count;

            PracticeSubtitle = seen == 0
                ? $"{total:N0} words · draws new ones each time"
                : $"{seen:N0} of {total:N0} words started";

            var learned = await _databases.Progress.CountLearnedAsync(scope);
            ShowMilestone(Milestones.Describe(learned));

            await RefreshBandsAsync(dictionary, pair);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Dictionary unavailable: {ex}");
            ErrorMessage = $"Could not open the dictionary: {ex.Message}";
            PracticeSubtitle = "Unavailable";
            HasMilestone = false;
        }
    }

    /// <summary>
    /// Lists the frequency bands with how many words each holds, for the current
    /// direction. Sizes come from the dictionary rather than being hard-coded, so
    /// regenerating it keeps them honest.
    /// </summary>
    private async Task RefreshBandsAsync(IDictionaryStore dictionary, LanguagePair pair)
    {
        var key = (pair.Id, IsReversed);
        if (!_bandCache.TryGetValue(key, out var rows))
        {
            rows = [];
            foreach (var band in FrequencyBand.For(pair, IsReversed))
            {
                var count = await dictionary.CountInBandAsync(band);
                if (count == 0)
                {
                    continue;
                }

                rows.Add(new BandRow(band, $"{count:N0} words · {band.RangeText}"));
            }

            _bandCache[key] = rows;
        }

        Bands.Clear();
        foreach (var row in rows)
        {
            Bands.Add(row);
        }
    }

    private void ShowMilestone(MilestoneProgress milestone)
    {
        MilestoneHeadline = $"{milestone.Learned:N0} / {milestone.Target:N0} words learned";

        // Deliberately terse: this sits beside the headline, and a longer phrasing pushes
        // the headline into wrapping onto two lines.
        MilestoneCaption = $"{milestone.Remaining:N0} to go";

        MilestoneFraction = milestone.Fraction;
        HasMilestone = true;
    }

    /// <summary>Switches the active pair and reloads everything scoped to it.</summary>
    [RelayCommand]
    private async Task SelectPairAsync(LanguagePair? pair)
    {
        if (pair is null || pair == SelectedPair || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            SelectedPair = pair;
            _settings.SelectedPairId = pair.Id;
            await RefreshProgressAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Flips between "en → de" and "de → en" (R-305). Applies to whichever pair is
    /// selected and reloads its bands and milestone from the reversed scope, which is
    /// completely separate progress from the forward direction.
    /// </summary>
    [RelayCommand]
    private async Task ToggleDirectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IsReversed = !IsReversed;
            _settings.PracticeReversed = IsReversed;
            await RefreshProgressAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task StartPracticeAsync()
        => SelectedPair is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync(
                $"{Routes.Session}?setId={Uri.EscapeDataString(PracticeSessionFactory.GeneratedSetIdFor(SelectedPair, IsReversed))}");

    [RelayCommand]
    private static Task StartBandAsync(BandRow? row)
        => row is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{Routes.Session}?setId={Uri.EscapeDataString(row.Band.Id)}");
}
