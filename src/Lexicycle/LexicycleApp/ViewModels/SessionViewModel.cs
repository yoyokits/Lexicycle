using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexicycleApp.Services;
using LexicycleCore.Dictionary;
using LexicycleCore.Models;
using LexicycleCore.Progress;
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
    private readonly AppDatabases _databases;

    private SessionEngine? _engine;

    /// <summary>Set only for dictionary-backed Practice sessions.</summary>
    private PracticeSessionFactory.PracticeSession? _practice;
    private PracticeSessionFactory? _factory;
    private LanguagePair? _pair;

    /// <summary>Set only for fixed sets — the bundled JSON, and later an OCR'd page.</summary>
    private FixedSetSessionFactory.FixedSetSession? _fixedSet;
    private FixedSetSessionFactory? _fixedSetFactory;

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

    /// <summary>
    /// True when a fixed set has been exhausted, so the only way on is to start it over.
    /// </summary>
    [ObservableProperty]
    private bool _canRestartSet;

    /// <summary>Remembered so "Start this set again" can reload after clearing progress.</summary>
    private string? _setId;

    public SessionViewModel(
        IVocabularySetRepository repository,
        AppSettings settings,
        AppDatabases databases)
    {
        _repository = repository;
        _settings = settings;
        _databases = databases;
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

    /// <summary>
    /// Whether there is a question on screen to answer. False when the set is exhausted
    /// or failed to load — an answer box reading "Type the translation" under no prompt
    /// at all invites the learner to type into nothing.
    /// </summary>
    public bool IsAnswering => !HasError && !IsLoading;

    public string ActionButtonText => AwaitingContinue ? "Next" : "Check";

    /// <summary>"End session" is the wrong words for a session that never started.</summary>
    public string QuitButtonText => IsAnswering ? "End session" : "Back to sets";

    partial void OnFeedbackTextChanged(string? value) => OnPropertyChanged(nameof(HasFeedback));

    partial void OnHintChanged(string? value) => OnPropertyChanged(nameof(HasHint));

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsAnswering));
        OnPropertyChanged(nameof(QuitButtonText));
    }

    partial void OnAwaitingContinueChanged(bool value)
    {
        OnPropertyChanged(nameof(CanType));
        OnPropertyChanged(nameof(ActionButtonText));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanType));
        OnPropertyChanged(nameof(IsAnswering));
        OnPropertyChanged(nameof(QuitButtonText));
    }

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
        CanRestartSet = false;
        _setId = setId;
        _fixedSet = null;

        var band = FrequencyBand.ById(setId);
        _pair = band is not null
            ? LanguagePair.ById(band.PairId)
            : PracticeSessionFactory.PairForGeneratedSetId(setId);
        var isGenerated = _pair is not null;

        try
        {
            var set = isGenerated
                ? await StartGeneratedSessionAsync(_pair!, band)
                : await StartFixedSetSessionAsync(setId);

            if (set is null)
            {
                ErrorMessage = $"Vocabulary set '{setId}' was not found.";
                return;
            }

            if (set.WordCount == 0)
            {
                // Every word has been asked. Nothing is replayed, so the set is finished
                // until the learner deliberately starts it over.
                if (_fixedSet is not null)
                {
                    ErrorMessage =
                        "You have been through every word in this set. " +
                        "Start it again to practise them a second time.";
                    CanRestartSet = true;
                }
                else if (band is not null)
                {
                    ErrorMessage =
                        $"You have learned every word in {band.Name} — {band.RangeText}. " +
                        "Move on to the next band.";
                }
                else
                {
                    ErrorMessage = "You have been through the whole dictionary. Nothing left to ask.";
                }

                return;
            }

            SetName = set.Name;

            // A generated session is already ordered most-common-first, which is the order
            // worth learning in. A fixed set has no frequency data, so its order is
            // randomised — otherwise "German basics" opens on the same word forever.
            _engine = new SessionEngine(
                isGenerated ? set : set.Shuffled(),
                _settings.CreateComparer());
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
    /// Draws the next batch of words from the dictionary, skipping anything practised
    /// recently so consecutive sessions differ.
    /// </summary>
    private async Task<VocabularySet> StartGeneratedSessionAsync(LanguagePair pair, FrequencyBand? band)
    {
        var dictionary = await _databases.GetDictionaryAsync(pair)
            ?? throw new InvalidOperationException($"No dictionary bundled for {pair.Id}.");

        _factory = new PracticeSessionFactory(pair, dictionary, _databases.Progress);

        _practice = await _factory.CreateAsync(band: band);
        return _practice.Set;
    }

    /// <summary>
    /// Draws a session from a bundled set, skipping what the last visit asked so opening
    /// the same set twice does not replay the same questions.
    /// </summary>
    private async Task<VocabularySet?> StartFixedSetSessionAsync(string setId)
    {
        var set = await _repository.GetByIdAsync(setId);
        if (set is null)
        {
            return null;
        }

        _fixedSetFactory = new FixedSetSessionFactory(_databases.Progress);
        _fixedSet = await _fixedSetFactory.CreateAsync(set);

        return _fixedSet.Set;
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
        var milestone = await SaveProgressAsync(summary);

        await DismissKeyboardAsync();

        var parameters = new Dictionary<string, object> { [Routes.SummaryParameter] = summary };
        if (milestone is not null)
        {
            parameters[Routes.MilestoneParameter] = milestone.Value;
        }

        await Shell.Current.GoToAsync($"{Routes.Summary}", parameters);
    }

    /// <summary>
    /// Remembers what was asked so the next generated session picks different words, and
    /// reports any milestone the session pushed the learner past.
    ///
    /// Failing to save must not cost the learner their summary screen, so everything here
    /// is best-effort. Bundled and OCR'd sets are not recorded at all, and so never raise
    /// a milestone — their words were chosen by hand and the rotation does not track them.
    /// </summary>
    private async Task<int?> SaveProgressAsync(SessionSummary summary)
    {
        try
        {
            // A fixed set records its results so the next visit asks different words, but
            // raises no milestone: the bar counts progress through the dictionary, and a
            // twelve-word bundled set is not progress through it.
            if (_fixedSet is not null && _fixedSetFactory is not null)
            {
                await _fixedSetFactory.RecordAsync(_fixedSet, summary);
                return null;
            }

            if (_practice is null || _factory is null || _pair is null)
            {
                return null;
            }

            var progress = _databases.Progress;
            var scope = ProgressScope.ForDictionary(_pair.Id);

            // Measured either side of the write, so the comparison covers exactly this
            // session and a milestone can only ever be celebrated once.
            var before = await progress.CountLearnedAsync(scope);
            await _factory.RecordAsync(_practice, summary);
            var after = await progress.CountLearnedAsync(scope);

            return Milestones.Crossed(before, after);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not save progress: {ex}");
            return null;
        }
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

    /// <summary>
    /// Clears this set's progress and reloads, so its words become new again. Scoped to
    /// the one set: dictionary progress and every other set are untouched.
    /// </summary>
    [RelayCommand]
    private async Task RestartSetAsync()
    {
        if (_fixedSet is null || _setId is null)
        {
            return;
        }

        try
        {
            await _databases.Progress.ResetScopeAsync(_fixedSet.Scope);
            await LoadAsync(_setId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not restart the set: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task QuitAsync()
    {
        await DismissKeyboardAsync();
        await Shell.Current.GoToAsync(Routes.Home);
    }
}
