using LexicycleApp.Views;

namespace LexicycleApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pages pushed onto the "home" ShellContent rather than being tabs of their own.
        Routing.RegisterRoute(Routes.Session, typeof(SessionPage));
        Routing.RegisterRoute(Routes.Summary, typeof(SummaryPage));
    }
}
