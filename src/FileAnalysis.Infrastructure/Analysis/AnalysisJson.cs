using System.Text.Json.Serialization;

namespace FileAnalysis.Infrastructure.Analysis;

/// <summary>Structured JSON payload requested from the Ollama analysis model.</summary>
internal sealed class AnalysisJson
{
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public string? Language { get; set; }
    public string[]? Tags { get; set; }
    public string[]? Topics { get; set; }

    [JsonPropertyName("usefulness_score")]
    public int? UsefulnessScore { get; set; }

    [JsonPropertyName("is_duplicate_candidate")]
    public bool? IsDuplicateCandidate { get; set; }
}
