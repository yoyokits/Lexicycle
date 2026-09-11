namespace LexicycleCore.Progress;

/// <summary>How one word went in a finished session.</summary>
public sealed record WordOutcome(int WordId, bool AnsweredCorrectly, int Misses);

/// <summary>
/// Identifies which pool of words a progress row belongs to.
///
/// Word ids are only unique within a pool: the dictionary numbers its words from
/// <c>words_en</c>, while a bundled set numbers its own words by position. Without a
/// scope, "word 3" of German basics and "word 3" of the dictionary would collide.
/// </summary>
public static class ProgressScope
{
    /// <summary>
    /// The generated English-German dictionary behind the original Practice session.
    /// Kept as a bare literal — not <c>ForDictionary("en-de")</c> — because every
    /// installed copy's existing progress was written under this exact key before
    /// language pairs existed; changing it would silently orphan real learners' history.
    /// </summary>
    public const string Dictionary = "dictionary";

    /// <summary>
    /// The generated dictionary for one language pair. English-German keeps the legacy
    /// unscoped key so existing progress is not orphaned; every other pair gets its own.
    /// </summary>
    public static string ForDictionary(string pairId)
        => pairId == "en-de" ? Dictionary : $"dictionary:{pairId}";

    /// <summary>A bundled or imported set is scoped by its own id.</summary>
    public static string ForSet(string setId) => $"set:{setId}";
}

/// <summary>
/// Remembers what has been practised, so consecutive sessions ask different words.
/// Lives in its own database file, separate from the generated dictionary, so shipping
/// an updated dictionary never wipes a learner's history.
///
/// Every method is scoped (see <see cref="ProgressScope"/>). Session numbering is
/// per-scope too: practising the dictionary must not advance German basics' rotation,
/// or the no-repeat rule would be satisfied by sessions the learner never played.
/// </summary>
public interface IProgressStore
{
    /// <summary>The number of sessions started so far in this scope.</summary>
    Task<int> GetSessionCountAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>Increments this scope's session counter and returns the new number.</summary>
    Task<int> BeginSessionAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>Progress for every word already practised in this scope.</summary>
    Task<IReadOnlyList<WordProgress>> GetAllAsync(
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many distinct words in this scope have been answered correctly in at least one
    /// session. This is what the milestone bar counts, so it is a query of its own rather
    /// than a pass over <see cref="GetAllAsync"/> — the home screen asks on every visit.
    /// </summary>
    Task<int> CountLearnedAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the results of a finished session, promoting or demoting each word
    /// according to <see cref="ReviewSchedule"/>.
    /// </summary>
    Task RecordAsync(
        string scope,
        int sessionNumber,
        IReadOnlyList<WordOutcome> outcomes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets one scope, including its session counter. A set drilled to exhaustion has
    /// nothing new left and so yields empty sessions; this is how the learner starts it
    /// over.
    /// </summary>
    Task ResetScopeAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>Forgets everything, in every scope. Exposed for a "reset progress" action.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
