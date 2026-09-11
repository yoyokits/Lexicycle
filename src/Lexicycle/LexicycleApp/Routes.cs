namespace LexicycleApp;

/// <summary>Shell route names, kept in one place so navigation strings cannot drift.</summary>
public static class Routes
{
    public const string Home = "//home";
    public const string Session = "session";
    public const string Summary = "summary";

    /// <summary>Key under which the finished session's summary is handed to the summary page.</summary>
    public const string SummaryParameter = "summary";

    /// <summary>
    /// Key carrying the milestone the session just passed, when it passed one. Absent
    /// otherwise, so the summary page shows nothing rather than a zero.
    /// </summary>
    public const string MilestoneParameter = "milestone";
}
