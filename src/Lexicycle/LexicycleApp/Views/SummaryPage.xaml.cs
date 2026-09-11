using LexicycleApp.ViewModels;

namespace LexicycleApp.Views;

public partial class SummaryPage : ContentPage
{
    private readonly SummaryViewModel _viewModel;

    private bool _celebrated;

    public SummaryPage(SummaryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CelebrateAsync();
    }

    /// <summary>
    /// The session is finished, so the hardware back button must not walk back into it.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(Routes.Home));
        return true;
    }

    /// <summary>
    /// Brings the milestone banner in: the card springs up, the trophy overshoots and
    /// settles with a short wobble, and the text follows a beat later.
    ///
    /// Runs once. Re-entering the page — which does not happen on the way forward, but
    /// can on a resume — would otherwise replay it.
    /// </summary>
    private async Task CelebrateAsync()
    {
        if (_celebrated || !_viewModel.HasMilestone)
        {
            return;
        }

        _celebrated = true;

        // A beat of stillness first, so the movement is noticed rather than missed
        // while the page is still settling in from the navigation transition.
        await Task.Delay(180);

        await Task.WhenAll(
            MilestoneBanner.FadeToAsync(1, 260, Easing.CubicOut),
            MilestoneBanner.ScaleToAsync(1.03, 260, Easing.CubicOut));

        await MilestoneBanner.ScaleToAsync(1.0, 140, Easing.CubicInOut);

        await Task.WhenAll(
            MilestoneText.FadeToAsync(1, 240, Easing.CubicOut),
            MilestoneText.TranslateToAsync(0, 0, 240, Easing.CubicOut),
            WobbleBadgeAsync());
    }

    /// <summary>A small celebratory shake of the trophy — two swings, decaying.</summary>
    private async Task WobbleBadgeAsync()
    {
        await MilestoneBadge.ScaleToAsync(1.35, 180, Easing.CubicOut);

        await MilestoneBadge.RotateToAsync(-14, 90, Easing.CubicInOut);
        await MilestoneBadge.RotateToAsync(12, 110, Easing.CubicInOut);
        await MilestoneBadge.RotateToAsync(-6, 90, Easing.CubicInOut);
        await MilestoneBadge.RotateToAsync(0, 80, Easing.CubicInOut);

        await MilestoneBadge.ScaleToAsync(1.0, 140, Easing.CubicInOut);
    }
}
