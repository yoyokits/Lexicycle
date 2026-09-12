using LexicycleCore.Models;

namespace LexicycleCore.Tests;

/// <summary>
/// What a miss reveals. A prompt carries every sense's translation (R-511), so several
/// answers are accepted and the learner has to be shown all of them — revealing one
/// implies the rest were wrong.
/// </summary>
public sealed class WordPairTests
{
    [Fact]
    public void All_answers_lists_every_accepted_translation_most_common_first()
    {
        var word = new WordPair("like", ["gern", "gern haben", "gefallen", "mögen"]);

        Assert.Equal("gern, gern haben, gefallen, mögen", word.AllAnswers);
    }

    [Fact]
    public void A_single_answer_reads_as_just_that_word()
    {
        var word = new WordPair("house", ["Haus"]);

        Assert.Equal("Haus", word.AllAnswers);
        Assert.Equal(word.PrimaryAnswer, word.AllAnswers);
    }

    [Fact]
    public void Every_answer_the_comparer_accepts_is_one_the_learner_was_shown()
    {
        // The point of the change: nothing may be graded correct that the miss message
        // failed to mention, or the feedback is teaching the wrong thing.
        var word = new WordPair("run", ["laufen", "rennen", "fließen"]);
        var comparer = new Session.AnswerComparer();

        foreach (var answer in word.AcceptableAnswers)
        {
            Assert.True(comparer.IsCorrect(word, answer), $"{answer} should be accepted");
            Assert.Contains(answer, word.AllAnswers);
        }
    }
}
