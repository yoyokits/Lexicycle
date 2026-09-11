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
///
/// Every read method also takes a <c>reversed</c> flag (R-305). Reversed queries ask the
/// dictionary backwards — target-language word in, English answers out — by swapping
/// which side of <c>translations</c> is grouped on. Nothing is precomputed for this: the
/// same <c>translations</c> rows already hold each English word's curated primary-sense
/// answers, and reading them the other way round is exactly the "what English word does
/// this translate?" question, with every English word that maps to it as an acceptable
/// answer.
/// </summary>
public sealed class SqliteDictionaryStore : IDictionaryStore, IAsyncDisposable
{
    /// <summary>Article shown in a hint, per gender, by target language. Spanish has no
    /// neuter for common nouns. Never includes the answer itself. Reversed questions get
    /// no hint at all — English carries no grammatical gender to hint at.</summary>
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

    public async Task<int> CountAsync(bool reversed = false, CancellationToken cancellationToken = default)
        => await _connection
            .ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM ({Ordered(reversed)})")
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<int>> GetUnseenIdsAsync(
        IReadOnlyCollection<int> excludeIds,
        int limit,
        FrequencyBand? band = null,
        bool reversed = false,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        var sql = $"SELECT id FROM ({BandWindow(band, reversed)})";

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
            .ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM ({BandWindow(band, band.Reversed)})")
            .ConfigureAwait(false);

    /// <summary>
    /// The band's words, most common first, as a subquery. A band's own
    /// <see cref="FrequencyBand.Reversed"/> normally decides direction (see
    /// <see cref="CountInBandAsync"/>); the explicit parameter exists so a null band (the
    /// whole dictionary) still has a direction to order by.
    ///
    /// A band is a positional window, so the ordering is applied first and the slice taken
    /// from it — filtering out already-seen words before slicing would shift the window
    /// and quietly change which words belong to the band.
    ///
    /// Offsets come from the band definitions, never from user input, so they are inlined
    /// — as the exclusion list above is.
    /// </summary>
    private string BandWindow(FrequencyBand? band, bool reversed)
    {
        var ordered = Ordered(reversed);

        if (band is null)
        {
            return ordered;
        }

        // SQLite requires a LIMIT before OFFSET; -1 means "no limit".
        var size = band.Size?.ToString() ?? "-1";
        return $"{ordered} LIMIT {size} OFFSET {band.Skip}";
    }

    /// <summary>
    /// Every drillable word's id, most common first, for one direction.
    ///
    /// Forward orders directly by <c>words_en.freq_rank</c>. Reversed has no equivalent
    /// column on the target side — the pipeline never ranks German or Spanish frequency
    /// on its own — so it approximates one: a target word's rank is the best (lowest)
    /// <c>freq_rank</c> among the English words that translate it. A target word that
    /// translates a common English word is, in practice, usually itself a common word.
    /// Excluding a target word entirely needs *every* linked English word to be unranked,
    /// which is rare — <c>HAVING</c> filters those out, mirroring forward's
    /// <c>WHERE freq_rank IS NOT NULL</c>.
    ///
    /// <c>id</c> breaks ties in both directions: <c>freq_rank</c> holds a scaled Zipf
    /// score with only a few hundred distinct values, so without a tiebreak the ordering
    /// is not stable and a band's membership could shift between queries.
    /// </summary>
    private string Ordered(bool reversed) => reversed
        ? $"""
           SELECT tw.id AS id FROM {_targetTable} tw
           JOIN translations t ON t.{_targetIdColumn} = tw.id
           JOIN words_en en ON en.id = t.en_id
           GROUP BY tw.id
           HAVING MIN(en.freq_rank) IS NOT NULL
           ORDER BY MIN(en.freq_rank), tw.id
           """
        : "SELECT id FROM words_en WHERE freq_rank IS NOT NULL ORDER BY freq_rank, id";

    public async Task<IReadOnlyList<DictionaryWord>> GetWordsAsync(
        IReadOnlyList<int> ids,
        bool reversed = false,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // One row per acceptable answer; grouped back into words below. Reversed swaps
        // which side is grouped on and drops gender — there is nothing to hint at when
        // the answer being typed is English.
        var sql = reversed
            ? $"""
               SELECT tw.id AS Id, tw.text AS Source, en.text AS Answer, NULL AS Gender,
                      en.freq_rank AS FreqRank
               FROM {_targetTable} tw
               JOIN translations t ON t.{_targetIdColumn} = tw.id
               JOIN words_en en ON en.id = t.en_id
               WHERE tw.id IN ({string.Join(",", ids)})
               ORDER BY tw.id, en.freq_rank, en.id
               """
            : $"""
               SELECT en.id AS Id, en.text AS Source, tw.text AS Answer, tw.gender AS Gender,
                      en.freq_rank AS FreqRank
               FROM words_en en
               JOIN translations t ON t.en_id = en.id
               JOIN {_targetTable} tw ON tw.id = t.{_targetIdColumn}
               WHERE en.id IN ({string.Join(",", ids)})
               ORDER BY en.id, tw.id
               """;

        var rows = await _connection.QueryAsync<PairRow>(sql).ConfigureAwait(false);

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
    /// Spelling out "das Haus" under the prompt "house" would hand over the answer. Null
    /// gender (always true for reversed questions) means no hint.
    /// </summary>
    private string? BuildHint(string? gender)
        => gender is not null && _articles.TryGetValue(gender, out var article)
            ? $"{article} … ({gender})"
            : null;

    public async ValueTask DisposeAsync() => await _connection.CloseAsync().ConfigureAwait(false);
}
