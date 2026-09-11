namespace LexicycleCore.Dictionary;

/// <summary>
/// A slice of one language pair's dictionary by commonness — "the 1,000 most common
/// words", and so on.
///
/// These replaced the hand-written starter sets that shipped in Phase 1. Those held
/// twelve words each, which was enough to demonstrate the trainer before the dictionary
/// existed and useless afterwards: a learner exhausted one in two sittings.
///
/// A band is a <b>window over the frequency ordering</b>, not a range of
/// <c>words_en.freq_rank</c> values. Despite its name that column is not an ordinal rank:
/// <c>src/python</c> writes <c>round((8 - zipf) × 1000)</c>, so it runs from about 1,590
/// to 6,990 with only ~480 distinct values across 3,545 words. Filtering on it directly
/// gives nonsense — "ranks 1-1000" matches nothing at all.
///
/// Windows also survive regeneration: whatever the dictionary grows to, "the first 1,000"
/// still means the first 1,000.
/// </summary>
/// <param name="Id">Globally unique across every pair and direction —
/// <c>"{pair.Id}:{suffix}"</c> forward, <c>"{pair.Id}:reverse:{suffix}"</c> reversed — so
/// it can be resolved back to the band, its pair and its direction from a bare route
/// parameter.</param>
/// <param name="Skip">How many more common words come before this band.</param>
/// <param name="Size">How many words the band holds. Null means "everything after Skip".</param>
/// <param name="PairId">The <see cref="LanguagePair"/> this band slices.</param>
/// <param name="Reversed">Whether this band orders and counts the pair's dictionary
/// backwards — target-language word in, English answer out (R-305). The windows
/// themselves (0-1,000, 1,000-2,000, ...) are the same shape either way; only which
/// ordering they slice differs. See <see cref="SqliteDictionaryStore"/>.</param>
public sealed record FrequencyBand(string Id, string Name, int Skip, int? Size, string PairId, bool Reversed = false)
{
    /// <summary>
    /// The window shape shared by every language pair and direction, in the order a
    /// learner should meet them. A thousand words is a real milestone — roughly the point
    /// at which ordinary conversation becomes followable — so the first band is sized to
    /// be worth finishing rather than sized to be quick.
    /// </summary>
    private static readonly (string Suffix, string Name, int Skip, int? Size)[] Windows =
    [
        ("basics", "Basics", 0, 1_000),
        ("common", "Common words", 1_000, 1_000),
        ("wider", "Wider vocabulary", 2_000, null),
    ];

    /// <summary>The bands for one language pair and direction, offered on the home screen.</summary>
    public static IReadOnlyList<FrequencyBand> For(LanguagePair pair, bool reversed = false)
    {
        var idPrefix = reversed ? $"{pair.Id}:reverse" : pair.Id;
        return Windows
            .Select(w => new FrequencyBand($"{idPrefix}:{w.Suffix}", w.Name, w.Skip, w.Size, pair.Id, reversed))
            .ToList();
    }

    /// <summary>Every band of every known pair and direction, for route resolution.</summary>
    public static IReadOnlyList<FrequencyBand> All { get; } =
        LanguagePair.All
            .SelectMany(pair => For(pair).Concat(For(pair, reversed: true)))
            .ToList();

    public static FrequencyBand? ById(string id)
        => All.FirstOrDefault(band => band.Id == id);

    /// <summary>1-based position of the first word, for display.</summary>
    public int FirstPosition => Skip + 1;

    /// <summary>Human-readable range, e.g. "the 1,000 most common words".</summary>
    public string RangeText => Skip == 0
        ? $"the {Size:N0} most common words"
        : Size is null
            ? $"word {FirstPosition:N0} onwards"
            : $"words {FirstPosition:N0}–{Skip + Size:N0} by commonness";
}
