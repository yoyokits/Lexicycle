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

    public HomeViewModel(AppSettings settings, AppDatabases databases)
    {
        _settings = settings;
        _databases = databases;
    }

    /// <summary>One frequency band on the home screen.</summary>
    public sealed record BandRow(FrequencyBand Band, string Subtitle)
    {
        public string Name => Band.Name;
    }

    public ObservableCollection<BandRow> Bands { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

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

    /// <summary>Shows how far through the dictionary the learner is, and towards the
    /// next milestone.</summary>
    private async Task RefreshProgressAsync()
    {
        try
        {
            var dictionary = await _databases.GetDictionaryAsync();
            var total = await dictionary.CountAsync();
            var seen = (await _databases.Progress.GetAllAsync(ProgressScope.Dictionary)).Count;

            PracticeSubtitle = seen == 0
                ? $"{total:N0} words · draws new ones each time"
                : $"{seen:N0} of {total:N0} words started";

            var learned = await _databases.Progress.CountLearnedAsync(ProgressScope.Dictionary);
            ShowMilestone(Milestones.Describe(learned));

            await RefreshBandsAsync(dictionary);
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
    /// Lists the frequency bands with how many words each holds. Sizes come from the
    /// dictionary rather than being hard-coded, so regenerating it keeps them honest.
    /// </summary>
    private async Task RefreshBandsAsync(IDictionaryStore dictionary)
    {
        if (Bands.Count > 0)
        {
            return;
        }

        foreach (var band in FrequencyBand.All)
        {
            var count = await dictionary.CountInBandAsync(band);
            if (count == 0)
            {
                continue;
            }

            Bands.Add(new BandRow(band, $"{count:N0} words · {band.RangeText}"));
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

    [RelayCommand]
    private static Task StartPracticeAsync()
        => Shell.Current.GoToAsync($"{Routes.Session}?setId={PracticeSessionFactory.GeneratedSetId}");

    [RelayCommand]
    private static Task StartBandAsync(BandRow? row)
        => row is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{Routes.Session}?setId={Uri.EscapeDataString(row.Band.Id)}");
}
