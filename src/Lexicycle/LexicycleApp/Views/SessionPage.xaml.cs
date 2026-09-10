using LexicycleApp.ViewModels;

namespace LexicycleApp.Views;

public partial class SessionPage : ContentPage
{
    private readonly SessionViewModel _viewModel;

    public SessionPage(SessionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Return focus to the box after every answer so the drill stays a typing loop.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.HideKeyboardAsync = HideKeyboardAsync;
    }

    /// <summary>
    /// Closes the soft keyboard and lets the window insets settle. Navigating with the
    /// IME still open leaves the incoming page measured to zero height on Android.
    /// </summary>
    private async Task HideKeyboardAsync()
    {
        AnswerEntry.Unfocus();

        if (AnswerEntry.IsSoftInputShowing())
        {
            await AnswerEntry.HideSoftInputAsync(CancellationToken.None);
        }

        // The insets animate; give them a frame or two before the next page measures.
        await Task.Delay(100);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        FocusAnswer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.CurrentWord))
        {
            FocusAnswer();
        }
    }

    /// <summary>
    /// Focusing straight away is unreliable on Android — the handler may not be attached
    /// yet, and the soft keyboard then never appears. A short hop to the next dispatcher
    /// pass gives the native view time to exist.
    /// </summary>
    private void FocusAnswer() => Dispatcher.Dispatch(async () =>
    {
        await Task.Delay(150);

        if (_viewModel.CanType)
        {
            AnswerEntry.Focus();
        }
    });
}
