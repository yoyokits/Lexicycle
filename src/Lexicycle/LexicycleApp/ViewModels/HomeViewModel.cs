using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexicycleApp.Services;
using LexicycleCore.Dictionary;
using LexicycleCore.Models;
using LexicycleCore.Services;

namespace LexicycleApp.ViewModels;

/// <summary>Lists the available vocabulary sets and starts a session.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IVocabularySetRepository _repository;
    private readonly AppSettings _settings;
    private readonly AppDatabases _databases;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _practiceSubtitle = "Draws new words each time";

    public HomeViewModel(
        IVocabularySetRepository repository,
        AppSettings settings,
        AppDatabases databases)
    {
        _repository = repository;
        _settings = settings;
        _databases = databases;
    }

    public ObservableCollection<VocabularySet> Sets { get; } = [];

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

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy || Sets.Count > 0)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var sets = await _repository.GetAllAsync();

            Sets.Clear();
            foreach (var set in sets)
            {
                Sets.Add(set);
            }

            await RefreshPracticeSubtitleAsync();
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

    /// <summary>Shows how far through the dictionary the learner is.</summary>
    private async Task RefreshPracticeSubtitleAsync()
    {
        try
        {
            var dictionary = await _databases.GetDictionaryAsync();
            var total = await dictionary.CountAsync();
            var seen = (await _databases.Progress.GetAllAsync()).Count;

            PracticeSubtitle = seen == 0
                ? $"{total:N0} words · draws new ones each time"
                : $"{seen:N0} of {total:N0} words started";
        }
        catch (Exception ex)
        {
            // The bundled sets still work without the dictionary, so this is not fatal.
            System.Diagnostics.Debug.WriteLine($"Dictionary unavailable: {ex}");
            PracticeSubtitle = "Draws new words each time";
        }
    }

    [RelayCommand]
    private static Task StartPracticeAsync()
        => Shell.Current.GoToAsync($"{Routes.Session}?setId={PracticeSessionFactory.GeneratedSetId}");

    [RelayCommand]
    private static Task StartAsync(VocabularySet? set)
        => set is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{Routes.Session}?setId={Uri.EscapeDataString(set.Id)}");
}
