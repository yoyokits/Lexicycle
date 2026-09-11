using LexicycleCore.Models;

namespace LexicycleCore.Tests;

public sealed class VocabularySetShuffleTests
{
    private static VocabularySet Set(int words) => new(
        "en-de-basics",
        "German basics",
        "en",
        "de",
        [.. Enumerable.Range(1, words).Select(i => new WordPair($"w{i:D2}", [$"W{i:D2}"]))]);

    [Fact]
    public void Shuffling_keeps_every_word_and_the_set_identity()
    {
        var original = Set(12);
        var shuffled = original.Shuffled(new Random(1));

        Assert.Equal(original.Id, shuffled.Id);
        Assert.Equal(original.Name, shuffled.Name);
        Assert.Equal(original.SourceLanguage, shuffled.SourceLanguage);
        Assert.Equal(original.TargetLanguage, shuffled.TargetLanguage);

        // Same words, no duplicates, none dropped.
        Assert.Equal(
            original.Words.Select(w => w.Source).OrderBy(s => s),
            shuffled.Words.Select(w => w.Source).OrderBy(s => s));
    }

    [Fact]
    public void Shuffling_does_not_modify_the_original()
    {
        var original = Set(12);
        var before = original.Words.Select(w => w.Source).ToList();

        original.Shuffled(new Random(2));

        Assert.Equal(before, original.Words.Select(w => w.Source));
    }

    [Fact]
    public void Consecutive_shuffles_of_a_fixed_set_differ()
    {
        // The behaviour this exists for: opening "German basics" twice must not present
        // the same sequence. Twelve words give 12! orderings, so a repeat across ten
        // draws would mean the shuffle is not happening at all.
        var set = Set(12);
        var orders = new HashSet<string>();

        for (var i = 0; i < 10; i++)
        {
            orders.Add(string.Join(" ", set.Shuffled().Words.Select(w => w.Source)));
        }

        Assert.True(orders.Count > 1, "every shuffle produced the same order");
    }

    [Fact]
    public void The_first_word_is_not_always_the_same_one()
    {
        // The most visible symptom: "German basics" always opened on "house".
        var set = Set(12);
        var firsts = new HashSet<string>();

        for (var i = 0; i < 40; i++)
        {
            firsts.Add(set.Shuffled().Words[0].Source);
        }

        Assert.True(firsts.Count > 1, $"the set always opened on the same word: {string.Join(",", firsts)}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Trivial_sets_shuffle_without_complaint(int words)
    {
        var shuffled = Set(words).Shuffled(new Random(3));

        Assert.Equal(words, shuffled.WordCount);
    }
}
