using LexicycleCore.Dictionary;
using LexicycleCore.Progress;

namespace LexicycleApp.Services;

/// <summary>
/// Owns the per-pair dictionary databases and the shared progress file, plus the
/// one-time copy of each bundled dictionary.
///
/// Every dictionary ships inside the package as a <c>MauiAsset</c>, which cannot be
/// opened as a file on Android, so each is copied to app data on first run. Progress
/// lives in a separate file so that shipping an updated dictionary never discards it.
/// </summary>
public sealed class AppDatabases : IAsyncDisposable
{
    private const string ProgressFile = "progress.db";

    /// <summary>
    /// Bumped when a new dictionary ships, so the copy is refreshed. Kept in preferences
    /// rather than compared by hash, which would mean reading the file on every launch.
    /// </summary>
    private const string InstalledVersionKey = "dictionary.installed_version";
    private const int DictionaryVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, SqliteDictionaryStore> _dictionaries = [];

    private SqliteProgressStore? _progress;

    /// <summary>
    /// Opens (copying in on first use) the dictionary for one pair. Returns null when
    /// that pair's database has not been generated yet — Spanish, until the pipeline is
    /// run for <c>en-es</c> and the file is bundled — so the caller can hide the option
    /// rather than crash on a missing asset.
    /// </summary>
    public async Task<IDictionaryStore?> GetDictionaryAsync(
        LanguagePair pair, CancellationToken cancellationToken = default)
    {
        if (_dictionaries.TryGetValue(pair.Id, out var existing))
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_dictionaries.TryGetValue(pair.Id, out existing))
            {
                return existing;
            }

            var path = await EnsureDictionaryCopiedAsync(pair).ConfigureAwait(false);
            if (path is null)
            {
                return null;
            }

            var store = new SqliteDictionaryStore(path, pair.TargetLanguage);
            _dictionaries[pair.Id] = store;
            return store;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IProgressStore Progress =>
        _progress ??= new SqliteProgressStore(
            Path.Combine(FileSystem.AppDataDirectory, ProgressFile));

    private static string AssetName(LanguagePair pair) => $"lexicycle-dict-{pair.Id}.db";

    /// <summary>Copies one pair's bundled dictionary into app data, or returns null if
    /// the package carries no asset for it.</summary>
    private static async Task<string?> EnsureDictionaryCopiedAsync(LanguagePair pair)
    {
        var asset = AssetName(pair);
        var destination = Path.Combine(FileSystem.AppDataDirectory, asset);
        var versionKey = $"{InstalledVersionKey}:{pair.Id}";
        var installed = Preferences.Default.Get(versionKey, 0);

        if (File.Exists(destination) && installed == DictionaryVersion)
        {
            return destination;
        }

        Stream source;
        try
        {
            source = await FileSystem.OpenAppPackageFileAsync(asset).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // Not every pair has been generated yet — see docs/DATA-SOURCES.md.
            return null;
        }

        await using (source)
        await using (var target = File.Create(destination))
        {
            await source.CopyToAsync(target).ConfigureAwait(false);
        }

        Preferences.Default.Set(versionKey, DictionaryVersion);
        return destination;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _dictionaries.Values)
        {
            await store.DisposeAsync().ConfigureAwait(false);
        }

        if (_progress is not null)
        {
            await _progress.DisposeAsync().ConfigureAwait(false);
        }
    }
}
