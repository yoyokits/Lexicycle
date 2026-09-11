using SQLite;

namespace LexicycleCore.Progress;

/// <summary>
/// Learner progress, in a writable database of its own so that shipping a new dictionary
/// never discards it.
/// </summary>
public sealed class SqliteProgressStore : IProgressStore, IAsyncDisposable
{
    private const string SessionCountKey = "session_count";

    private readonly SQLiteAsyncConnection _connection;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialised;

    public SqliteProgressStore(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    /// <summary>
    /// One word's progress within one scope.
    ///
    /// The real key is (scope, word_id), but sqlite-net supports only a single primary
    /// key column, so the two are also stored joined in <see cref="Key"/>. Scope and
    /// WordId remain separate columns because every query filters on scope.
    /// </summary>
    [Table("word_progress_v2")]
    private sealed class ProgressRow
    {
        [PrimaryKey, Column("key")]
        public string Key { get; set; } = string.Empty;

        [Indexed, Column("scope")]
        public string Scope { get; set; } = string.Empty;

        [Column("word_id")]
        public int WordId { get; set; }

        [Column("box")]
        public int Box { get; set; }

        [Column("times_seen")]
        public int TimesSeen { get; set; }

        [Column("times_correct")]
        public int TimesCorrect { get; set; }

        [Column("times_missed")]
        public int TimesMissed { get; set; }

        [Column("last_session")]
        public int LastSession { get; set; }

        public static string KeyFor(string scope, int wordId) => $"{scope}#{wordId}";

        public WordProgress ToProgress()
            => new(WordId, Box, TimesSeen, TimesCorrect, TimesMissed, LastSession);
    }

    [Table("progress_meta")]
    private sealed class MetaRow
    {
        [PrimaryKey, Column("key")]
        public string Key { get; set; } = string.Empty;

        [Column("value")]
        public int Value { get; set; }
    }

    private async Task EnsureReadyAsync()
    {
        if (_initialised)
        {
            return;
        }

        await _initGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialised)
            {
                return;
            }

            await _connection.CreateTableAsync<ProgressRow>().ConfigureAwait(false);
            await _connection.CreateTableAsync<MetaRow>().ConfigureAwait(false);
            await MigrateFromUnscopedAsync().ConfigureAwait(false);
            _initialised = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Carries rows over from the original unscoped <c>word_progress</c> table, which had
    /// no scope column and held dictionary words only. Runs once: the old table is dropped
    /// afterwards, so a second call finds nothing to do.
    /// </summary>
    private async Task MigrateFromUnscopedAsync()
    {
        var legacy = await _connection
            .ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'word_progress'")
            .ConfigureAwait(false);

        if (legacy == 0)
        {
            return;
        }

        await _connection.ExecuteAsync(
            $"""
             INSERT OR IGNORE INTO word_progress_v2
                 (key, scope, word_id, box, times_seen, times_correct, times_missed, last_session)
             SELECT '{ProgressScope.Dictionary}#' || word_id, '{ProgressScope.Dictionary}',
                    word_id, box, times_seen, times_correct, times_missed, last_session
             FROM word_progress
             """).ConfigureAwait(false);

        // The old global session counter becomes the dictionary's counter.
        await _connection.ExecuteAsync(
            $"""
             INSERT OR IGNORE INTO progress_meta (key, value)
             SELECT '{SessionCountKey}:{ProgressScope.Dictionary}', value
             FROM progress_meta WHERE key = '{SessionCountKey}'
             """).ConfigureAwait(false);

        await _connection.ExecuteAsync("DROP TABLE word_progress").ConfigureAwait(false);
        await _connection
            .ExecuteAsync($"DELETE FROM progress_meta WHERE key = '{SessionCountKey}'")
            .ConfigureAwait(false);
    }

    private static string CounterKey(string scope) => $"{SessionCountKey}:{scope}";

    public async Task<int> GetSessionCountAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        var row = await _connection
            .FindAsync<MetaRow>(CounterKey(scope))
            .ConfigureAwait(false);

        return row?.Value ?? 0;
    }

    public async Task<int> BeginSessionAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        var next = await GetSessionCountAsync(scope, cancellationToken).ConfigureAwait(false) + 1;

        await _connection
            .InsertOrReplaceAsync(new MetaRow { Key = CounterKey(scope), Value = next })
            .ConfigureAwait(false);

        return next;
    }

    public async Task<IReadOnlyList<WordProgress>> GetAllAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        var rows = await _connection
            .Table<ProgressRow>()
            .Where(row => row.Scope == scope)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToProgress()).ToList();
    }

    public async Task<int> CountLearnedAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        return await _connection
            .ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM word_progress_v2 WHERE scope = ? AND times_correct > 0",
                scope)
            .ConfigureAwait(false);
    }

    public async Task RecordAsync(
        string scope,
        int sessionNumber,
        IReadOnlyList<WordOutcome> outcomes,
        CancellationToken cancellationToken = default)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        await EnsureReadyAsync().ConfigureAwait(false);

        foreach (var outcome in outcomes)
        {
            var key = ProgressRow.KeyFor(scope, outcome.WordId);

            var row = await _connection.FindAsync<ProgressRow>(key).ConfigureAwait(false)
                ?? new ProgressRow { Key = key, Scope = scope, WordId = outcome.WordId };

            row.Box = ReviewSchedule.NextBox(row.Box, outcome.AnsweredCorrectly);
            row.TimesSeen++;
            row.TimesMissed += outcome.Misses;
            row.LastSession = sessionNumber;

            if (outcome.AnsweredCorrectly)
            {
                row.TimesCorrect++;
            }

            await _connection.InsertOrReplaceAsync(row).ConfigureAwait(false);
        }
    }

    public async Task ResetScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        await _connection
            .ExecuteAsync("DELETE FROM word_progress_v2 WHERE scope = ?", scope)
            .ConfigureAwait(false);

        // The counter goes too, so the restarted set begins at session 1.
        await _connection
            .ExecuteAsync("DELETE FROM progress_meta WHERE key = ?", CounterKey(scope))
            .ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        await _connection.DeleteAllAsync<ProgressRow>().ConfigureAwait(false);
        await _connection.DeleteAllAsync<MetaRow>().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _connection.CloseAsync().ConfigureAwait(false);
}
