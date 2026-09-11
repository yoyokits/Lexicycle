using LexicycleCore.Models;

namespace LexicycleCore.Dictionary;

/// <summary>One dictionary entry, resolved into a drillable question.</summary>
/// <param name="FreqRank">
/// 1 is the most common word on whichever side is being asked *from* — English for a
/// forward question, an English-frequency heuristic (see
/// <see cref="SqliteDictionaryStore"/>) for a reversed one. Null for the unranked tail.
/// Carried through so a session can be presented most-common-first, which is the order
/// worth learning in.
/// </param>
public sealed record DictionaryWord(
    int Id,
    string Source,
    IReadOnlyList<string> Answers,
    string? Hint,
    int? FreqRank = null)
{
    public WordPair ToWordPair() => new(Source, Answers, Hint);
}

/// <summary>
/// One language pair's generated dictionary, read only. Built by <c>src/python</c>; see
/// docs/DATA-SOURCES.md.
///
/// Every method takes a <c>reversed</c> flag: <c>false</c> asks "what is the target-
/// language word for this English one?" (the original direction); <c>true</c> asks the
/// opposite — "what is the English word for this target-language one?" (R-305). The two
/// directions are different pools with different id spaces (English word ids forward,
/// target-language word ids reversed), so a caller must not mix ids from one direction
/// into a call for the other.
/// </summary>
public interface IDictionaryStore
{
    /// <summary>How many drillable words the dictionary holds in this direction.</summary>
    Task<int> CountAsync(bool reversed = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Word ids never yet practised, most frequent first, so a learner meets useful
    /// vocabulary before obscure vocabulary.
    /// </summary>
    /// <param name="band">
    /// Restricts the draw to one slice of the frequency ranking. Null draws from the whole
    /// dictionary. When given, its own <see cref="FrequencyBand.Reversed"/> decides the
    /// direction and the <paramref name="reversed"/> parameter is ignored.
    /// </param>
    Task<IReadOnlyList<int>> GetUnseenIdsAsync(
        IReadOnlyCollection<int> excludeIds,
        int limit,
        FrequencyBand? band = null,
        bool reversed = false,
        CancellationToken cancellationToken = default);

    /// <summary>How many drillable words fall inside one frequency band, in that band's
    /// own direction.</summary>
    Task<int> CountInBandAsync(FrequencyBand band, CancellationToken cancellationToken = default);

    /// <summary>Resolves ids into questions, preserving the order asked for.</summary>
    Task<IReadOnlyList<DictionaryWord>> GetWordsAsync(
        IReadOnlyList<int> ids,
        bool reversed = false,
        CancellationToken cancellationToken = default);
}
