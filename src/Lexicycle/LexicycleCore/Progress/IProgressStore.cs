namespace LexicycleCore.Progress;

/// <summary>How one word went in a finished session.</summary>
public sealed record WordOutcome(int WordId, bool AnsweredCorrectly, int Misses);

/// <summary>
/// Remembers what has been practised, so consecutive sessions ask different words.
/// Lives in its own database file, separate from the generated dictionary, so shipping
/// an updated dictionary never wipes a learner's history.
/// </summary>
public interface IProgressStore
{
    /// <summary>The number of sessions started so far.</summary>
    Task<int> GetSessionCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Increments the session counter and returns the new session's number.</summary>
    Task<int> BeginSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Progress for every word already practised.</summary>
    Task<IReadOnlyList<WordProgress>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many distinct words have been answered correctly in at least one session.
    /// This is what the milestone bar counts, so it is a query of its own rather than a
    /// pass over <see cref="GetAllAsync"/> — the home screen asks for it on every visit.
    /// </summary>
    Task<int> CountLearnedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the results of a finished session, promoting or demoting each word
    /// according to <see cref="ReviewSchedule"/>.
    /// </summary>
    Task RecordAsync(
        int sessionNumber,
        IReadOnlyList<WordOutcome> outcomes,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets everything. Exposed for a "reset progress" action.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
