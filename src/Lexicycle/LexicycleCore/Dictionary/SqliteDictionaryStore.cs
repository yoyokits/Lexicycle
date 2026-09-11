using SQLite;

namespace LexicycleCore.Dictionary;

/// <summary>
/// Reads one language pair's generated dictionary. The file is produced by
/// <c>src/python</c> and shipped read-only, so this opens it without ever creating
/// tables.
///
/// Every pair's database shares the <c>words_en</c> table shape, but the target side is
/// named after the language (<c>words_de</c>/<c>de_id</c>, <c>words_es</c>/<c>es_id</c>),
/// so the target language has to be known at construction time to build the right SQL.
/// The language code comes from <see cref="LanguagePair"/>, never from user input, so
/// interpolating it into table and column names is as safe as the band offsets below.
/// </summary>
public sealed class SqliteDictionaryStore : IDictionaryStore, IAsyncDisposable
{
    /// <summary>Article shown in a hint, per gender, by target language. Spanish has no
    /// neuter for common nouns. Never includes the answer itself.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> ArticlesByLanguage = new()
    {
        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["masculine"] = "der",
            ["feminine"] = "die",
            ["neuter"] = "das",
        },
        ["es"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["masculine"] = "el",
            ["feminine"] = "la",
        },
    };

    private readonly SQLiteAsyncConnection _connection;
    private readonly string _targetTable;
    private readonly string _targetIdColumn;
    private readonly Dictionary<string, string> _articles;

    public SqliteDictionaryStore(string databasePath, string targetLanguage)
    {
        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        _targetTable = $"words_{targetLanguage}";
        _targetIdColumn = $"{targetLanguage}_id";
        _articles = ArticlesByLanguage.TryGetValue(targetLanguage, out var articles)
            ? articles
            : [];
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
        FrequencyBand? band = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        // Unranked words are the rare tail of the dictionary; they make poor questions.
        var sql = $"SELECT id FROM ({BandWindow(band)})";

        if (excludeIds.Count > 0)
        {
            sql += $" WHERE id NOT IN ({string.Join(",", excludeIds)})";
        }

        sql += " LIMIT ?";

        var rows = await _connection.QueryAsync<IdRow>(sql, limit).ConfigureAwait(false);
        return rows.Select(row => row.Id).ToList();
    }

    public async Task<int> CountInBandAsync(
        FrequencyBand band,
        CancellationToken cancellationToken = default)
        => await _connection
            .ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM ({BandWindow(band)})")
            .ConfigureAwait(false);

    /// <summary>
    /// The band's words, most common first, as a subquery.
    ///
    /// A band is a positional window, so the ordering is applied first and the slice taken
    /// from it — filtering out already-seen words before slicing would shift the window
    /// and quietly change which words belong to the band.
    ///
    /// <c>id</c> breaks ties: <c>freq_rank</c> holds a scaled Zipf score with only a few
    /// hundred distinct values, so without a tiebreak the ordering is not stable and a
    /// band's membership could shift between queries.
    ///
    /// Offsets come from the band definitions, never from user input, so they are inlined
    /// — as the exclusion list above is.
    /// </summary>
    private static string BandWindow(FrequencyBand? band)
    {
        const string Ordered =
            "SELECT id FROM words_en WHERE freq_rank IS NOT NULL ORDER BY freq_rank, id";

        if (band is null)
        {
            return Ordered;
        }

        // SQLite requires a LIMIT before OFFSET; -1 means "no limit".
        var size = band.Size?.ToString() ?? "-1";
        return $"{Ordered} LIMIT {size} OFFSET {band.Skip}";
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
             SELECT en.id AS Id, en.text AS Source, tw.text AS Answer, tw.gender AS Gender,
                    en.freq_rank AS FreqRank
             FROM words_en en
             JOIN translations t ON t.en_id = en.id
             JOIN {_targetTable} tw ON tw.id = t.{_targetIdColumn}
             WHERE en.id IN ({string.Join(",", ids)})
             ORDER BY en.id, tw.id
             """).ConfigureAwait(false);

        var byId = rows
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, BuildWord);

        // Preserve the caller's ordering; a missing id simply drops out.
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private DictionaryWord BuildWord(IGrouping<int, PairRow> group)
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
    private string? BuildHint(string? gender)
        => gender is not null && _articles.TryGetValue(gender, out var article)
            ? $"{article} … ({gender})"
            : null;

    public async ValueTask DisposeAsync() => await _connection.CloseAsync().ConfigureAwait(false);
}
