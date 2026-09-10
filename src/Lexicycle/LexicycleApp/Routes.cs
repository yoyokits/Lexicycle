namespace LexicycleApp;

/// <summary>Shell route names, kept in one place so navigation strings cannot drift.</summary>
public static class Routes
{
    public const string Home = "//home";
    public const string Session = "session";
    public const string Summary = "summary";

    /// <summary>Key under which the finished session's summary is handed to the summary page.</summary>
    public const string SummaryParameter = "summary";
}
