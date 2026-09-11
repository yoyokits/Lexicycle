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
}
