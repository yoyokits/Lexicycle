namespace LexicycleCore.Dictionary;

/// <summary>
/// A slice of the dictionary by commonness — "the 1,000 most common words", and so on.
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
/// <param name="Skip">How many more common words come before this band.</param>
/// <param name="Size">How many words the band holds. Null means "everything after Skip".</param>
public sealed record FrequencyBand(string Id, string Name, int Skip, int? Size)
{
    /// <summary>
    /// The bands offered on the home screen, in the order a learner should meet them.
    ///
    /// A thousand words is a real milestone — roughly the point at which ordinary
    /// conversation becomes followable — so the first band is sized to be worth finishing
    /// rather than sized to be quick.
    /// </summary>
    public static IReadOnlyList<FrequencyBand> All { get; } =
    [
        new("band-1", "Basics", 0, 1_000),
        new("band-2", "Common words", 1_000, 1_000),
        new("band-3", "Wider vocabulary", 2_000, null),
    ];

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
