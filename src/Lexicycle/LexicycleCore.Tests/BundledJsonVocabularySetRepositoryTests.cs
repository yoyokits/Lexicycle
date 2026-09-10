using System.Text;
using LexicycleCore.Services;

namespace LexicycleCore.Tests;

public class BundledJsonVocabularySetRepositoryTests
{
    /// <summary>Serves JSON from memory, standing in for the app package.</summary>
    private sealed class FakeAssets(Dictionary<string, string> files) : IAssetProvider
    {
        public int OpenCount { get; private set; }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            OpenCount++;

            if (!files.TryGetValue(relativePath, out var json))
            {
                throw new FileNotFoundException(relativePath);
            }

            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        }
    }

    private const string ValidSet = """
        {
          "id": "en-de-test",
          "name": "Test set",
          "sourceLanguage": "en",
          "targetLanguage": "de",
          "words": [
            { "source": "house", "answers": ["Haus"], "hint": "das" },
            { "source": "car", "answers": ["Auto", "Wagen"] }
          ]
        }
        """;

    [Fact]
    public async Task Loads_a_set_from_json()
    {
        var assets = new FakeAssets(new() { ["sets/a.json"] = ValidSet });
        var repository = new BundledJsonVocabularySetRepository(assets, ["sets/a.json"]);

        var sets = await repository.GetAllAsync();

        var set = Assert.Single(sets);
        Assert.Equal("en-de-test", set.Id);
        Assert.Equal("en", set.SourceLanguage);
        Assert.Equal("de", set.TargetLanguage);
        Assert.Equal(2, set.WordCount);

        Assert.Equal("das", set.Words[0].Hint);
        Assert.Equal(["Auto", "Wagen"], set.Words[1].AcceptableAnswers);
        Assert.Equal("Auto", set.Words[1].PrimaryAnswer);
    }

    [Fact]
    public async Task Caches_after_the_first_read()
    {
        var assets = new FakeAssets(new() { ["sets/a.json"] = ValidSet });
        var repository = new BundledJsonVocabularySetRepository(assets, ["sets/a.json"]);

        await repository.GetAllAsync();
        await repository.GetAllAsync();
        await repository.GetByIdAsync("en-de-test");

        Assert.Equal(1, assets.OpenCount);
    }

    [Fact]
    public async Task Finds_a_set_by_id_ignoring_case()
    {
        var assets = new FakeAssets(new() { ["sets/a.json"] = ValidSet });
        var repository = new BundledJsonVocabularySetRepository(assets, ["sets/a.json"]);

        Assert.NotNull(await repository.GetByIdAsync("EN-DE-TEST"));
        Assert.Null(await repository.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task Rejects_a_set_with_no_words()
    {
        var assets = new FakeAssets(new()
        {
            ["bad.json"] = """{ "id": "x", "name": "x", "words": [] }"""
        });
        var repository = new BundledJsonVocabularySetRepository(assets, ["bad.json"]);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAllAsync());
    }

    [Fact]
    public async Task Rejects_an_entry_without_answers()
    {
        var assets = new FakeAssets(new()
        {
            ["bad.json"] = """{ "id": "x", "words": [ { "source": "house" } ] }"""
        });
        var repository = new BundledJsonVocabularySetRepository(assets, ["bad.json"]);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetAllAsync());
    }
}
