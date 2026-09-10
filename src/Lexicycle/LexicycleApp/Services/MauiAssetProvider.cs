using LexicycleCore.Services;

namespace LexicycleApp.Services;

/// <summary>Reads bundled <c>MauiAsset</c> files out of the app package.</summary>
public sealed class MauiAssetProvider : IAssetProvider
{
    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        => await FileSystem.OpenAppPackageFileAsync(relativePath).ConfigureAwait(false);
}
