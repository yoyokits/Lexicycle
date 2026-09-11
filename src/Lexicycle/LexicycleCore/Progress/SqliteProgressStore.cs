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

    [Table("word_progress")]
    private sealed class ProgressRow
    {
        [PrimaryKey, Column("word_id")]
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
            _initialised = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<int> GetSessionCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        var row = await _connection
            .FindAsync<MetaRow>(SessionCountKey)
            .ConfigureAwait(false);

        return row?.Value ?? 0;
    }

    public async Task<int> BeginSessionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        var next = await GetSessionCountAsync(cancellationToken).ConfigureAwait(false) + 1;

        await _connection
            .InsertOrReplaceAsync(new MetaRow { Key = SessionCountKey, Value = next })
            .ConfigureAwait(false);

        return next;
    }

    public async Task<IReadOnlyList<WordProgress>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        var rows = await _connection.Table<ProgressRow>().ToListAsync().ConfigureAwait(false);
        return rows.Select(row => row.ToProgress()).ToList();
    }

    public async Task RecordAsync(
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
            var row = await _connection.FindAsync<ProgressRow>(outcome.WordId).ConfigureAwait(false)
                ?? new ProgressRow { WordId = outcome.WordId };

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

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync().ConfigureAwait(false);

        await _connection.DeleteAllAsync<ProgressRow>().ConfigureAwait(false);
        await _connection.DeleteAllAsync<MetaRow>().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _connection.CloseAsync().ConfigureAwait(false);
}
