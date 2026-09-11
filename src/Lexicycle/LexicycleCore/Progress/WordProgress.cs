namespace LexicycleCore.Progress;

/// <summary>
/// What the app remembers about one dictionary word between sessions.
/// </summary>
/// <param name="WordId">Identifier in the dictionary's <c>words_en</c> table.</param>
/// <param name="Box">
/// Leitner box. 0 means "being learned"; each correct answer promotes it one box and
/// lengthens the wait before it is asked again.
/// </param>
/// <param name="TimesSeen">How many sessions have asked this word.</param>
/// <param name="TimesCorrect">Sessions where it was eventually answered correctly.</param>
/// <param name="TimesMissed">Total misses across all rounds of all sessions.</param>
/// <param name="LastSession">Session number in which it was last asked.</param>
public sealed record WordProgress(
    int WordId,
    int Box,
    int TimesSeen,
    int TimesCorrect,
    int TimesMissed,
    int LastSession)
{
    public static WordProgress New(int wordId) => new(wordId, 0, 0, 0, 0, 0);

    /// <summary>
    /// Wrong answers given for this word, across every round of every session.
    /// This is what drives the revision weighting in <see cref="SessionComposer"/>.
    /// </summary>
    public int TimesAnsweredWrong => TimesMissed;

    /// <summary>
    /// Right answers given for this word. A session only ends once every one of its words
    /// has been answered correctly, so this is one per session the word appeared in —
    /// which is why it equals <see cref="TimesSeen"/> rather than being counted
    /// separately. <see cref="TimesCorrect"/> is the stricter figure: sessions where the
    /// word was right *first try*, with no misses at all.
    /// </summary>
    public int TimesAnsweredRight => TimesSeen;

    /// <summary>
    /// Share of answers that were right, 0-1. Null for a word never yet asked, so that
    /// "no data" is distinguishable from "always wrong".
    /// </summary>
    public double? Accuracy => TimesSeen == 0
        ? null
        : (double)TimesAnsweredRight / (TimesAnsweredRight + TimesAnsweredWrong);
}
