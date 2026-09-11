using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexicycleCore.Session;

namespace LexicycleApp.ViewModels;

/// <summary>One row on the summary screen.</summary>
public sealed record MissedWordRow(string Source, string Answer, int MissCount)
{
    public string MissText => MissCount == 1 ? "missed once" : $"missed {MissCount} times";
}

/// <summary>End-of-session results.</summary>
public sealed partial class SummaryViewModel : ObservableObject, IQueryAttributable
{
    [ObservableProperty]
    private string _headline = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private bool _wasPerfect;

    [ObservableProperty]
    private bool _hasMilestone;

    [ObservableProperty]
    private string _milestoneTitle = string.Empty;

    [ObservableProperty]
    private string _milestoneMessage = string.Empty;

    public ObservableCollection<MissedWordRow> MissedWords { get; } = [];

    public bool HasMissedWords => MissedWords.Count > 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ApplyMilestone(query);

        if (!query.TryGetValue(Routes.SummaryParameter, out var value) || value is not SessionSummary summary)
        {
            return;
        }

        WasPerfect = summary.WasPerfect;
        Headline = summary.WasPerfect ? "Flawless!" : "Session complete";

        var rounds = summary.RoundsTaken == 1 ? "1 round" : $"{summary.RoundsTaken} rounds";
        Detail = summary.WasPerfect
            ? $"All {summary.TotalWords} words correct on the first try, in {rounds}."
            : $"{summary.TotalWords} words learned in {rounds}.";

        MissedWords.Clear();
        foreach (var score in summary.MissedWords)
        {
            MissedWords.Add(new MissedWordRow(score.Word.Source, score.Word.PrimaryAnswer, score.MissCount));
        }

        OnPropertyChanged(nameof(HasMissedWords));
    }

    /// <summary>
    /// Sets up the congratulation when this session pushed the learner past a milestone.
    /// The parameter is absent the rest of the time, which is the overwhelming majority
    /// of sessions — that rarity is what makes it worth celebrating.
    /// </summary>
    private void ApplyMilestone(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(Routes.MilestoneParameter, out var raw) || raw is not int milestone)
        {
            HasMilestone = false;
            return;
        }

        MilestoneTitle = $"{milestone:N0} words learned";
        MilestoneMessage = Encouragement(milestone);
        HasMilestone = true;
    }

    private static string Encouragement(int milestone) => milestone switch
    {
        10 => "Your first milestone. The hardest one is behind you.",
        50 => "Fifty words is enough to start recognising them in the wild.",
        100 => "Triple figures. That is a real vocabulary now.",
        500 => "Five hundred words covers most of an ordinary conversation.",
        1000 => "A thousand words. This is what fluency is built on.",
        _ => $"{milestone:N0} words and still going. Remarkable.",
    };

    [RelayCommand]
    private static Task DoneAsync() => Shell.Current.GoToAsync(Routes.Home);
}
