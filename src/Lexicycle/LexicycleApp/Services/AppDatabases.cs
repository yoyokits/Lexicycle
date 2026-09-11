using LexicycleCore.Dictionary;
using LexicycleCore.Progress;

namespace LexicycleApp.Services;

/// <summary>
/// Owns the two database files and the one-time copy of the bundled dictionary.
///
/// The dictionary ships inside the package as a <c>MauiAsset</c>, which cannot be opened
/// as a file on Android, so it is copied to app data on first run. Progress lives in a
/// separate file so that shipping an updated dictionary never discards it.
/// </summary>
public sealed class AppDatabases : IAsyncDisposable
{
    private const string DictionaryAsset = "lexicycle-dict-en-de.db";
    private const string ProgressFile = "progress.db";

    /// <summary>
    /// Bumped when a new dictionary ships, so the copy is refreshed. Kept in preferences
    /// rather than compared by hash, which would mean reading 536 KB on every launch.
    /// </summary>
    private const string InstalledVersionKey = "dictionary.installed_version";
    private const int DictionaryVersion = 1;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private SqliteDictionaryStore? _dictionary;
    private SqliteProgressStore? _progress;

    public async Task<IDictionaryStore> GetDictionaryAsync(CancellationToken cancellationToken = default)
    {
        if (_dictionary is not null)
        {
            return _dictionary;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _dictionary ??= new SqliteDictionaryStore(await EnsureDictionaryCopiedAsync().ConfigureAwait(false));
            return _dictionary;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IProgressStore Progress =>
        _progress ??= new SqliteProgressStore(
            Path.Combine(FileSystem.AppDataDirectory, ProgressFile));

    private static async Task<string> EnsureDictionaryCopiedAsync()
    {
        var destination = Path.Combine(FileSystem.AppDataDirectory, DictionaryAsset);
        var installed = Preferences.Default.Get(InstalledVersionKey, 0);

        if (File.Exists(destination) && installed == DictionaryVersion)
        {
            return destination;
        }

        await using var source = await FileSystem.OpenAppPackageFileAsync(DictionaryAsset).ConfigureAwait(false);
        await using (var target = File.Create(destination))
        {
            await source.CopyToAsync(target).ConfigureAwait(false);
        }

        Preferences.Default.Set(InstalledVersionKey, DictionaryVersion);
        return destination;
    }

    public async ValueTask DisposeAsync()
    {
        if (_dictionary is not null)
        {
            await _dictionary.DisposeAsync().ConfigureAwait(false);
        }

        if (_progress is not null)
        {
            await _progress.DisposeAsync().ConfigureAwait(false);
        }
    }
}
