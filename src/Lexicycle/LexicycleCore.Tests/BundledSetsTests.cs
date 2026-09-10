using LexicycleCore.Models;
using LexicycleCore.Services;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

/// <summary>
/// Validates the JSON sets the app actually ships, copied into the test output by the
/// csproj. Guards against a hand-edited set breaking the app at runtime.
/// </summary>
public class BundledSetsTests
{
    private sealed class DiskAssets : IAssetProvider
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(File.OpenRead(relativePath));
    }

    private static readonly string[] SetFiles = Directory.Exists("sets")
        ? Directory.GetFiles("sets", "*.json").Order().ToArray()
        : [];

    public static TheoryData<string> AllSets()
    {
        var data = new TheoryData<string>();
        foreach (var file in SetFiles)
        {
            data.Add(file);
        }

        return data;
    }

    private static async Task<VocabularySet> LoadAsync(string path)
    {
        var repository = new BundledJsonVocabularySetRepository(new DiskAssets(), [path]);
        var sets = await repository.GetAllAsync();
        return sets.Single();
    }

    [Fact]
    public void The_bundled_sets_are_present()
    {
        // If this fails the csproj stopped copying them and every other test here is vacuous.
        Assert.NotEmpty(SetFiles);
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task Every_set_parses(string path)
    {
        var set = await LoadAsync(path);

        Assert.False(string.IsNullOrWhiteSpace(set.Id));
        Assert.False(string.IsNullOrWhiteSpace(set.Name));
        Assert.NotEqual("??", set.SourceLanguage);
        Assert.NotEqual("??", set.TargetLanguage);
        Assert.NotEmpty(set.Words);
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task No_hint_gives_away_its_own_answer(string path)
    {
        var set = await LoadAsync(path);

        foreach (var word in set.Words.Where(w => !string.IsNullOrEmpty(w.Hint)))
        {
            foreach (var answer in word.AcceptableAnswers)
            {
                Assert.False(
                    word.Hint!.Contains(answer, StringComparison.CurrentCultureIgnoreCase),
                    $"The hint for '{word.Source}' in {Path.GetFileName(path)} contains the answer '{answer}'.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task Prompts_are_unique_within_a_set(string path)
    {
        var set = await LoadAsync(path);
        var duplicates = set.Words
            .GroupBy(word => word.Source, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task Every_answer_is_accepted_by_the_comparer(string path)
    {
        var set = await LoadAsync(path);
        var comparer = new AnswerComparer();

        foreach (var word in set.Words)
        {
            foreach (var answer in word.AcceptableAnswers)
            {
                Assert.True(
                    comparer.IsCorrect(word, answer),
                    $"'{answer}' should be accepted for '{word.Source}'.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task A_full_correct_run_completes_in_one_round(string path)
    {
        var set = await LoadAsync(path);
        var engine = new SessionEngine(set);

        while (!engine.IsComplete)
        {
            engine.Submit(engine.CurrentWord!.PrimaryAnswer);
        }

        var summary = engine.BuildSummary();
        Assert.Equal(1, summary.RoundsTaken);
        Assert.True(summary.WasPerfect);
    }
}
