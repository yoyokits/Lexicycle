using SQLite;

namespace LexicycleCore.Dictionary;

/// <summary>
/// Reads the generated dictionary. The file is produced by <c>src/python</c> and shipped
/// read-only, so this opens it without ever creating tables.
/// </summary>
public sealed class SqliteDictionaryStore : IDictionaryStore, IAsyncDisposable
{
    /// <summary>Article shown in a hint, per gender. Never includes the answer itself.</summary>
    private static readonly Dictionary<string, string> Articles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["masculine"] = "der",
        ["feminine"] = "die",
        ["neuter"] = "das",
    };

    private readonly SQLiteAsyncConnection _connection;

    public SqliteDictionaryStore(string databasePath)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
    }

    private sealed class IdRow
    {
        public int Id { get; set; }
    }

    private sealed class PairRow
    {
        public int Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string? Gender { get; set; }
        public int? FreqRank { get; set; }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        => await _connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM words_en WHERE freq_rank IS NOT NULL").ConfigureAwait(false);

    public async Task<IReadOnlyList<int>> GetUnseenIdsAsync(
        IReadOnlyCollection<int> excludeIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        // Unranked words are the rare tail of the dictionary; they make poor questions.
        var sql = "SELECT id FROM words_en WHERE freq_rank IS NOT NULL";

        if (excludeIds.Count > 0)
        {
            sql += $" AND id NOT IN ({string.Join(",", excludeIds)})";
        }

        sql += " ORDER BY freq_rank LIMIT ?";

        var rows = await _connection.QueryAsync<IdRow>(sql, limit).ConfigureAwait(false);
        return rows.Select(row => row.Id).ToList();
    }

    public async Task<IReadOnlyList<DictionaryWord>> GetWordsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // One row per acceptable answer; grouped back into words below.
        var rows = await _connection.QueryAsync<PairRow>(
            $"""
             SELECT en.id AS Id, en.text AS Source, de.text AS Answer, de.gender AS Gender,
                    en.freq_rank AS FreqRank
             FROM words_en en
             JOIN translations t ON t.en_id = en.id
             JOIN words_de de ON de.id = t.de_id
             WHERE en.id IN ({string.Join(",", ids)})
             ORDER BY en.id, de.id
             """).ConfigureAwait(false);

        var byId = rows
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, BuildWord);

        // Preserve the caller's ordering; a missing id simply drops out.
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private static DictionaryWord BuildWord(IGrouping<int, PairRow> group)
    {
        var answers = group.Select(row => row.Answer).ToList();
        var first = group.First();

        return new DictionaryWord(
            group.Key, first.Source, answers, BuildHint(first.Gender), first.FreqRank);
    }

    /// <summary>
    /// A gender hint that gives the article but never the word — "das … (neuter)".
    /// Spelling out "das Haus" under the prompt "house" would hand over the answer.
    /// </summary>
    private static string? BuildHint(string? gender)
        => gender is not null && Articles.TryGetValue(gender, out var article)
            ? $"{article} … ({gender})"
            : null;

    public async ValueTask DisposeAsync() => await _connection.CloseAsync().ConfigureAwait(false);
}
