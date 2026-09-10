using System.Text.Json;
using LexicycleCore.Models;

namespace LexicycleCore.Services;

/// <summary>
/// Loads vocabulary sets from JSON files shipped in the app package.
/// Results are cached after the first successful read — bundled sets never change at runtime.
/// </summary>
public sealed class BundledJsonVocabularySetRepository : IVocabularySetRepository
{
    private readonly IAssetProvider _assets;
    private readonly IReadOnlyList<string> _assetPaths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<VocabularySet>? _cache;

    public BundledJsonVocabularySetRepository(IAssetProvider assets, IReadOnlyList<string> assetPaths)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _assetPaths = assetPaths ?? throw new ArgumentNullException(nameof(assetPaths));
    }

    public async Task<IReadOnlyList<VocabularySet>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var sets = new List<VocabularySet>(_assetPaths.Count);
            foreach (var path in _assetPaths)
            {
                sets.Add(await LoadAsync(path, cancellationToken).ConfigureAwait(false));
            }

            _cache = sets;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VocabularySet?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var sets = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return sets.FirstOrDefault(set => string.Equals(set.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<VocabularySet> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = await _assets.OpenAsync(path, cancellationToken).ConfigureAwait(false);

        var dto = await JsonSerializer
            .DeserializeAsync(stream, VocabularySetJsonContext.Default.VocabularySetDto, cancellationToken)
            .ConfigureAwait(false);

        if (dto is null)
        {
            throw new InvalidDataException($"Vocabulary set '{path}' is empty or not valid JSON.");
        }

        return MapSet(dto, path);
    }

    private static VocabularySet MapSet(VocabularySetDto dto, string path)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidDataException($"Vocabulary set '{path}' is missing an id.");
        }

        if (dto.Words is null || dto.Words.Count == 0)
        {
            throw new InvalidDataException($"Vocabulary set '{path}' contains no words.");
        }

        var words = new List<WordPair>(dto.Words.Count);
        foreach (var word in dto.Words)
        {
            if (string.IsNullOrWhiteSpace(word.Source) || word.Answers is null || word.Answers.Count == 0)
            {
                throw new InvalidDataException(
                    $"Vocabulary set '{path}' has an entry without a source term or answers.");
            }

            words.Add(new WordPair(word.Source, word.Answers, word.Hint));
        }

        return new VocabularySet(
            dto.Id,
            dto.Name ?? dto.Id,
            dto.SourceLanguage ?? "??",
            dto.TargetLanguage ?? "??",
            words);
    }
}
