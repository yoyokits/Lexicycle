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

    // --- the article is optional, in both directions --------------------------------------

    [Theory]
    [InlineData("das Haus", "Haus")]
    [InlineData("Haus", "das Haus")]
    [InlineData("der Hund", "Hund")]
    [InlineData("die Katze", "Katze")]
    [InlineData("Katze", "die Katze")]
    [InlineData("DAS HAUS", "haus")]
    [InlineData("  das   Haus  ", "Haus")]
    public void A_leading_article_may_be_given_or_omitted(string typed, string expected)
    {
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(new WordPair("house", [expected]), typed));
    }

    [Theory]
    [InlineData("la casa", "casa")]
    [InlineData("casa", "la casa")]
    [InlineData("el perro", "perro")]
    [InlineData("un amigo", "amigo")]
    public void Spanish_articles_are_optional_too(string typed, string expected)
    {
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(new WordPair("house", [expected]), typed));
    }

    [Fact]
    public void The_article_rule_composes_with_the_diacritics_rules()
    {
        var comparer = new AnswerComparer(lenientDiacritics: true);

        Assert.True(comparer.IsCorrect(new WordPair("street", ["die Straße"]), "strasse"));
        Assert.True(comparer.IsCorrect(new WordPair("girl", ["Mädchen"]), "das Madchen"));
    }

    [Fact]
    public void The_article_stays_optional_in_strict_diacritics_mode()
    {
        // Article optionality is independent of how diacritics are treated.
        var comparer = new AnswerComparer(lenientDiacritics: false);

        Assert.True(comparer.IsCorrect(new WordPair("house", ["das Haus"]), "Haus"));
    }

    [Fact]
    public void A_bare_article_is_still_a_word_in_its_own_right()
    {
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(new WordPair("the", ["die"]), "die"));
        Assert.False(comparer.IsCorrect(new WordPair("the", ["die"]), "das"));
    }

    [Fact]
    public void Only_a_real_article_is_dropped()
    {
        var comparer = new AnswerComparer();

        // "sich" is not an article, so a reflexive verb keeps both of its words.
        Assert.False(comparer.IsCorrect(new WordPair("to be glad", ["sich freuen"]), "freuen"));
    }

    [Fact]
    public void A_wrong_article_does_not_make_a_wrong_noun_right()
    {
        var comparer = new AnswerComparer();

        Assert.False(comparer.IsCorrect(new WordPair("house", ["das Haus"]), "das Hund"));
    }

    [Fact]
    public void The_article_may_differ_from_the_expected_one()
    {
        // Gender is taught by the hint, not enforced by the grader: the learner is being
        // asked for the noun, so "der Haus" still counts as knowing "Haus".
        var comparer = new AnswerComparer();

        Assert.True(comparer.IsCorrect(new WordPair("house", ["das Haus"]), "der Haus"));
    }
}
