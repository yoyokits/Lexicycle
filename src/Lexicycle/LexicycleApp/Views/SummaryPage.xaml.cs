using LexicycleApp.ViewModels;

namespace LexicycleApp.Views;

public partial class SummaryPage : ContentPage
{
    public SummaryPage(SummaryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// The session is finished, so the hardware back button must not walk back into it.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(Routes.Home));
        return true;
    }
}
