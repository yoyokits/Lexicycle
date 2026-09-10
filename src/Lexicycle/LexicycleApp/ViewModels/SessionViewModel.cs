using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexicycleApp.Services;
using LexicycleCore.Services;
using LexicycleCore.Session;

namespace LexicycleApp.ViewModels;

/// <summary>
/// Drives the practice screen. All the round logic lives in <see cref="SessionEngine"/>;
/// this class only turns engine state into bindable properties.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject, IQueryAttributable
{
    private readonly IVocabularySetRepository _repository;
    private readonly AppSettings _settings;

    private SessionEngine? _engine;

    [ObservableProperty]
    private string _setName = string.Empty;

    [ObservableProperty]
    private string _currentWord = string.Empty;

    [ObservableProperty]
    private string? _hint;

    [ObservableProperty]
    private string _answer = string.Empty;

    [ObservableProperty]
    private string _roundText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _feedbackText;

    [ObservableProperty]
    private bool _feedbackIsMiss;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// True while a miss is on screen. The user has to acknowledge it, so the correct
    /// answer stays up long enough to read.
    /// </summary>
    [ObservableProperty]
    private bool _awaitingContinue;

    public SessionViewModel(IVocabularySetRepository repository, AppSettings settings)
    {
        _repository = repository;
        _settings = settings;
    }

    /// <summary>
    /// Supplied by the page to close the soft keyboard before navigating away.
    ///
    /// Android lays the incoming page out against the still-open IME insets and ends up
    /// giving it zero height, so the summary screen arrives blank until the keyboard
    /// happens to close. Dismissing it first avoids that entirely.
    /// </summary>
    public Func<Task>? HideKeyboardAsync { get; set; }

    public bool HasFeedback => !string.IsNullOrEmpty(FeedbackText);

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The entry is locked while a miss is being acknowledged.</summary>
    public bool CanType => !AwaitingContinue && !IsLoading;

    public string ActionButtonText => AwaitingContinue ? "Next" : "Check";

    partial void OnFeedbackTextChanged(string? value) => OnPropertyChanged(nameof(HasFeedback));

    partial void OnHintChanged(string? value) => OnPropertyChanged(nameof(HasHint));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnAwaitingContinueChanged(bool value)
    {
        OnPropertyChanged(nameof(CanType));
        OnPropertyChanged(nameof(ActionButtonText));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanType));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("setId", out var raw) || raw?.ToString() is not { } setId)
        {
            ErrorMessage = "No vocabulary set was selected.";
            IsLoading = false;
            return;
        }

        _ = LoadAsync(Uri.UnescapeDataString(setId));
    }

    private async Task LoadAsync(string setId)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var set = await _repository.GetByIdAsync(setId);
            if (set is null)
            {
                ErrorMessage = $"Vocabulary set '{setId}' was not found.";
                return;
            }

            SetName = set.Name;
            _engine = new SessionEngine(set, _settings.CreateComparer());
            RefreshFromEngine();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not start the session: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// One button drives both steps: grade the typed answer, or dismiss a miss and move on.
    /// The Enter key is wired to the same command.
    /// </summary>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (_engine is null || IsLoading)
        {
            return;
        }

        if (AwaitingContinue)
        {
            AwaitingContinue = false;
            FeedbackText = null;
            await ContinueOrFinishAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(Answer))
        {
            return;
        }

        var result = _engine.Submit(Answer);
        Answer = string.Empty;

        if (result.IsCorrect)
        {
            FeedbackIsMiss = false;
            FeedbackText = "Correct";
            await ContinueOrFinishAsync();
            return;
        }

        // On a miss, hold the screen until the user taps Next so the answer can be read.
        FeedbackIsMiss = true;
        FeedbackText = $"{result.Word.Source} → {result.CorrectAnswer}";
        AwaitingContinue = true;
    }

    private async Task ContinueOrFinishAsync()
    {
        if (_engine!.IsComplete)
        {
            await GoToSummaryAsync();
            return;
        }

        RefreshFromEngine();
    }

    private async Task GoToSummaryAsync()
    {
        var summary = _engine!.BuildSummary();

        await DismissKeyboardAsync();

        await Shell.Current.GoToAsync(
            $"{Routes.Summary}",
            new Dictionary<string, object> { [Routes.SummaryParameter] = summary });
    }

    private async Task DismissKeyboardAsync()
    {
        if (HideKeyboardAsync is null)
        {
            return;
        }

        await HideKeyboardAsync();
    }

    private void RefreshFromEngine()
    {
        if (_engine is null || _engine.CurrentWord is null)
        {
            return;
        }

        // Clear the previous word's verdict; leaving "Correct" up under a fresh prompt
        // reads as if the new word had already been answered.
        FeedbackText = null;

        CurrentWord = _engine.CurrentWord.Source;
        Hint = _engine.CurrentWord.Hint;
        RoundText = $"Round {_engine.RoundNumber}";
        ProgressText = $"Word {_engine.PositionInRound} of {_engine.WordsInRound}";
        Progress = _engine.WordsInRound == 0
            ? 0
            : (double)(_engine.PositionInRound - 1) / _engine.WordsInRound;
    }

    [RelayCommand]
    private async Task QuitAsync()
    {
        await DismissKeyboardAsync();
        await Shell.Current.GoToAsync(Routes.Home);
    }
}
