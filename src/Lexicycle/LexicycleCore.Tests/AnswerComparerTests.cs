using LexicycleCore.Models;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

public class AnswerComparerTests
{
    private static WordPair Pair(params string[] answers) => new("house", answers);

    [Theory]
    [InlineData("Haus")]
    [InlineData("haus")]
    [InlineData("HAUS")]
    [InlineData("  Haus  ")]
    [InlineData("\tHaus\n")]
    public void Accepts_regardless_of_case_and_surrounding_whitespace(string typed)
    {
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(Pair("Haus"), typed));
    }

    [Fact]
    public void Collapses_repeated_internal_whitespace()
    {
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(new WordPair("ice cream", ["Eis Creme"]), "Eis    Creme"));
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Wagen")]
    public void Accepts_any_of_several_valid_translations(string typed)
    {
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(new WordPair("car", ["Auto", "Wagen"]), typed));
    }

    [Fact]
    public void Rejects_a_wrong_answer()
    {
        var comparer = new AnswerComparer();

        Assert.False(comparer.IsCorrect(Pair("Haus"), "Hund"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_answer(string? typed)
    {
        var comparer = new AnswerComparer();

        Assert.False(comparer.IsCorrect(Pair("Haus"), typed));
    }

    // --- the diacritics toggle, both ways -------------------------------------------------

    [Theory]
    [InlineData("Cafe", "Café")]
    [InlineData("Madchen", "Mädchen")]
    [InlineData("Maedchen", "Mädchen")]  // German ASCII fallback
    [InlineData("Strasse", "Straße")]
    [InlineData("Fuesse", "Füße")]
    public void Lenient_mode_ignores_diacritics(string typed, string expected)
    {
        var comparer = new AnswerComparer(lenientDiacritics: true);

        Assert.True(comparer.IsCorrect(Pair(expected), typed));
    }

    [Theory]
    [InlineData("Cafe", "Café")]
    [InlineData("Madchen", "Mädchen")]
    [InlineData("Strasse", "Straße")]
    public void Strict_mode_requires_exact_diacritics(string typed, string expected)
    {
        var comparer = new AnswerComparer(lenientDiacritics: false);

        Assert.False(comparer.IsCorrect(Pair(expected), typed));
    }

    [Fact]
    public void Strict_mode_still_accepts_the_exact_spelling_in_any_case()
    {
        var comparer = new AnswerComparer(lenientDiacritics: false);

        Assert.True(comparer.IsCorrect(Pair("Mädchen"), "mädchen"));
    }

    [Fact]
    public void Lenient_is_the_default()
    {
        Assert.True(new AnswerComparer().LenientDiacritics);
    }
}
