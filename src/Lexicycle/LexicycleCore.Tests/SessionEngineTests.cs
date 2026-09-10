using LexicycleCore.Models;
using LexicycleCore.Session;

namespace LexicycleCore.Tests;

public class SessionEngineTests
{
    private static VocabularySet Set(params (string Source, string Answer)[] words) =>
        new(
            "test-set",
            "Test set",
            "en",
            "de",
            words.Select(w => new WordPair(w.Source, [w.Answer])).ToList());

    private static readonly VocabularySet ThreeWords = Set(
        ("house", "Haus"),
        ("dog", "Hund"),
        ("car", "Auto"));

    // --- correct answers retire the word ---------------------------------------------------

    [Fact]
    public void A_correct_answer_retires_the_word_from_the_session()
    {
        var engine = new SessionEngine(ThreeWords);

        engine.Submit("Haus");  // house, correct
        engine.Submit("Hund");  // dog, correct
        engine.Submit("Falsch"); // car, missed

        // Round two contains only the missed word; the two correct ones are gone.
        Assert.Equal(2, engine.RoundNumber);
        Assert.Equal(1, engine.WordsInRound);
        Assert.Equal("car", engine.CurrentWord!.Source);
    }

    [Fact]
    public void A_perfect_first_round_completes_the_session()
    {
        var engine = new SessionEngine(ThreeWords);

        engine.Submit("Haus");
        engine.Submit("Hund");
        var last = engine.Submit("Auto");

        Assert.True(last.IsCorrect);
        Assert.True(last.RoundCompleted);
        Assert.True(last.SessionCompleted);
        Assert.True(engine.IsComplete);
        Assert.Null(engine.CurrentWord);
    }

    // --- misses are deferred to the next round, never re-asked immediately ------------------

    [Fact]
    public void A_missed_word_is_not_re_asked_immediately()
    {
        var engine = new SessionEngine(ThreeWords);

        var result = engine.Submit("wrong"); // house, missed

        Assert.False(result.IsCorrect);
        Assert.Equal("Haus", result.CorrectAnswer);

        // The very next prompt is the following word, not the one just missed.
        Assert.Equal("dog", engine.CurrentWord!.Source);
        Assert.Equal(1, engine.RoundNumber);
    }

    [Fact]
    public void Missed_words_form_the_next_round_in_the_order_they_were_missed()
    {
        var engine = new SessionEngine(ThreeWords);

        engine.Submit("wrong");  // house, missed
        engine.Submit("Hund");   // dog, correct
        engine.Submit("wrong");  // car, missed

        Assert.Equal(2, engine.RoundNumber);
        Assert.Equal(2, engine.WordsInRound);
        Assert.Equal("house", engine.CurrentWord!.Source);

        engine.Submit("Haus");
        Assert.Equal("car", engine.CurrentWord!.Source);
    }

    [Fact]
    public void Every_word_in_a_round_is_asked_exactly_once()
    {
        var engine = new SessionEngine(ThreeWords);
        var asked = new List<string>();

        while (!engine.IsComplete && engine.RoundNumber == 1)
        {
            asked.Add(engine.CurrentWord!.Source);
            engine.Submit("deliberately wrong");
        }

        Assert.Equal(["house", "dog", "car"], asked);
    }

    // --- session completes only after a clean round ----------------------------------------

    [Fact]
    public void The_session_ends_only_when_a_full_round_passes_with_zero_misses()
    {
        var engine = new SessionEngine(ThreeWords);

        // Round 1: miss everything.
        engine.Submit("x");
        engine.Submit("x");
        engine.Submit("x");
        Assert.False(engine.IsComplete);
        Assert.Equal(2, engine.RoundNumber);

        // Round 2: get two right, miss one — still not done.
        engine.Submit("Haus");
        engine.Submit("Hund");
        engine.Submit("x");
        Assert.False(engine.IsComplete);
        Assert.Equal(3, engine.RoundNumber);
        Assert.Equal(1, engine.WordsInRound);

        // Round 3: clean round, session over.
        engine.Submit("Auto");
        Assert.True(engine.IsComplete);
    }

    [Fact]
    public void An_empty_set_is_complete_before_it_starts()
    {
        var engine = new SessionEngine(Set());

        Assert.True(engine.IsComplete);
        Assert.Null(engine.CurrentWord);
    }

    [Fact]
    public void Submitting_after_completion_throws()
    {
        var engine = new SessionEngine(Set(("house", "Haus")));
        engine.Submit("Haus");

        Assert.Throws<InvalidOperationException>(() => engine.Submit("Haus"));
    }

    // --- progress reporting ----------------------------------------------------------------

    [Fact]
    public void Reports_position_within_the_round()
    {
        var engine = new SessionEngine(ThreeWords);

        Assert.Equal(1, engine.PositionInRound);
        Assert.Equal(3, engine.WordsInRound);

        engine.Submit("Haus");
        Assert.Equal(2, engine.PositionInRound);
    }

    // --- summary ---------------------------------------------------------------------------

    [Fact]
    public void Summary_reports_rounds_taken_and_ranks_the_hardest_words()
    {
        var engine = new SessionEngine(ThreeWords);

        // Round 1: house and car missed, dog correct.
        engine.Submit("x");
        engine.Submit("Hund");
        engine.Submit("x");

        // Round 2: house missed again, car correct.
        engine.Submit("x");
        engine.Submit("Auto");

        // Round 3: house finally correct.
        engine.Submit("Haus");

        var summary = engine.BuildSummary();

        Assert.True(engine.IsComplete);
        Assert.Equal(3, summary.RoundsTaken);
        Assert.Equal(3, summary.TotalWords);
        Assert.False(summary.WasPerfect);

        Assert.Equal("house", summary.HardestWords[0].Word.Source);
        Assert.Equal(2, summary.HardestWords[0].MissCount);

        Assert.Equal(["house", "car"], summary.MissedWords.Select(m => m.Word.Source));
    }

    [Fact]
    public void Summary_of_a_flawless_session_is_perfect()
    {
        var engine = new SessionEngine(ThreeWords);

        engine.Submit("Haus");
        engine.Submit("Hund");
        engine.Submit("Auto");

        var summary = engine.BuildSummary();

        Assert.True(summary.WasPerfect);
        Assert.Empty(summary.MissedWords);
        Assert.Equal(1, summary.RoundsTaken);
    }

    // --- the comparer is honoured ----------------------------------------------------------

    [Fact]
    public void Uses_the_supplied_comparer_for_grading()
    {
        var set = Set(("girl", "Mädchen"));

        var lenient = new SessionEngine(set, new AnswerComparer(lenientDiacritics: true));
        Assert.True(lenient.Submit("Madchen").IsCorrect);

        var strict = new SessionEngine(set, new AnswerComparer(lenientDiacritics: false));
        Assert.False(strict.Submit("Madchen").IsCorrect);
    }
}
