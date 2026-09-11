using LexicycleCore.Models;

namespace LexicycleCore.Dictionary;

/// <summary>One dictionary entry, resolved into a drillable question.</summary>
/// <param name="FreqRank">
/// 1 is the most common English word. Null for the unranked tail. Carried through so a
/// session can be presented most-common-first, which is the order worth learning in.
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
/// The generated English-German dictionary, read only.
/// Built by <c>src/python</c>; see docs/DATA-SOURCES.md.
/// </summary>
public interface IDictionaryStore
{
    /// <summary>How many drillable words the dictionary holds.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Word ids never yet practised, most frequent first, so a learner meets useful
    /// vocabulary before obscure vocabulary.
    /// </summary>
    Task<IReadOnlyList<int>> GetUnseenIdsAsync(
        IReadOnlyCollection<int> excludeIds,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves ids into questions, preserving the order asked for.</summary>
    Task<IReadOnlyList<DictionaryWord>> GetWordsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken = default);
}
