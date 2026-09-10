using LexicycleCore.Models;

namespace LexicycleCore.Services;

/// <summary>
/// Source of vocabulary sets. Bundled JSON backs v1; a SQLite-backed implementation
/// for the generated Wiktionary dictionary slots in behind the same interface.
/// </summary>
public interface IVocabularySetRepository
{
    Task<IReadOnlyList<VocabularySet>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<VocabularySet?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Opens files that ship inside the app package. Keeps <see cref="LexicycleCore"/> free
/// of any MAUI dependency, so the repository is testable with in-memory streams.
/// </summary>
public interface IAssetProvider
{
    Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default);
}
