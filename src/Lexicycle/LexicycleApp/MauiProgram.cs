using LexicycleApp.Services;
using LexicycleApp.ViewModels;
using LexicycleApp.Views;
using LexicycleCore.Services;
using Microsoft.Extensions.Logging;

namespace LexicycleApp;

public static class MauiProgram
{
    /// <summary>
    /// No sets ship as JSON any more. The starter files held twelve words each — enough
    /// to demonstrate the trainer before the dictionary existed, and useless once it did.
    /// Vocabulary now comes from the dictionary, sliced into frequency bands.
    ///
    /// The repository seam stays for the sets OCR will produce (R-404).
    /// </summary>
    private static readonly string[] BundledSets = [];

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Services
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<AppDatabases>();
        builder.Services.AddSingleton<IAssetProvider, MauiAssetProvider>();
        builder.Services.AddSingleton<IVocabularySetRepository>(provider =>
            new BundledJsonVocabularySetRepository(
                provider.GetRequiredService<IAssetProvider>(),
                BundledSets));

        // Pages and view models. Transient so each navigation starts from clean state.
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<SessionViewModel>();
        builder.Services.AddTransient<SessionPage>();
        builder.Services.AddTransient<SummaryViewModel>();
        builder.Services.AddTransient<SummaryPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
