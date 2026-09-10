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

    public ObservableCollection<MissedWordRow> MissedWords { get; } = [];

    public bool HasMissedWords => MissedWords.Count > 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
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

    [RelayCommand]
    private static Task DoneAsync() => Shell.Current.GoToAsync(Routes.Home);
}
