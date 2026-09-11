using System.ComponentModel;
using LexicycleApp.ViewModels;

namespace LexicycleApp.Views;

public partial class HomePage : ContentPage
{
    private const uint FillDuration = 600;

    private readonly HomeViewModel _viewModel;

    /// <summary>
    /// Animate only after the first paint. Sliding the bar up from zero on a cold start
    /// would suggest the learner had just earned every one of those words.
    /// </summary>
    private bool _barDrawn;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        MilestoneTrack.SizeChanged += (_, _) => UpdateMilestoneBar(animated: false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeViewModel.MilestoneFraction))
        {
            UpdateMilestoneBar(animated: _barDrawn);
        }
    }

    /// <summary>
    /// Sizes the fill against the measured track. A width of zero means the track has not
    /// been laid out yet; <c>SizeChanged</c> will call back once it has.
    /// </summary>
    private void UpdateMilestoneBar(bool animated)
    {
        var track = MilestoneTrack.Width;
        if (track <= 0)
        {
            return;
        }

        var target = track * Math.Clamp(_viewModel.MilestoneFraction, 0, 1);
        _barDrawn = true;

        if (!animated)
        {
            MilestoneFill.WidthRequest = target;
            return;
        }

        var from = MilestoneFill.WidthRequest is var current && current >= 0 ? current : 0;

        MilestoneFill.Animate(
            "milestoneFill",
            new Animation(width => MilestoneFill.WidthRequest = width, from, target),
            length: FillDuration,
            easing: Easing.CubicOut);
    }
}
