using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexicycleApp.Services;
using LexicycleCore.Models;
using LexicycleCore.Services;

namespace LexicycleApp.ViewModels;

/// <summary>Lists the available vocabulary sets and starts a session.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IVocabularySetRepository _repository;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public HomeViewModel(IVocabularySetRepository repository, AppSettings settings)
    {
        _repository = repository;
        _settings = settings;
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

    [RelayCommand]
    private static Task StartAsync(VocabularySet? set)
        => set is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{Routes.Session}?setId={Uri.EscapeDataString(set.Id)}");
}
