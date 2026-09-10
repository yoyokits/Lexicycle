using System.Text.Json;
using System.Text.Json.Serialization;

namespace LexicycleCore.Services;

/// <summary>Wire shape of a bundled vocabulary set JSON file.</summary>
internal sealed class VocabularySetDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public List<WordPairDto>? Words { get; set; }
}

internal sealed class WordPairDto
{
    public string? Source { get; set; }
    public List<string>? Answers { get; set; }
    public string? Hint { get; set; }
}

/// <summary>
/// Source-generated serialization context. Reflection-based System.Text.Json is
/// stripped by the trimmer in Release Android builds, so the generated one is required.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(VocabularySetDto))]
internal sealed partial class VocabularySetJsonContext : JsonSerializerContext;
