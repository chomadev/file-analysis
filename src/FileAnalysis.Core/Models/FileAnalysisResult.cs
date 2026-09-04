namespace FileAnalysis.Core.Models;

/// <summary>AI-generated analysis of a file (populated in Phase 3).</summary>
public class FileAnalysisResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public string? Language { get; set; }
    public string[] Tags { get; set; } = [];
    public string[] Topics { get; set; } = [];
    public int? UsefulnessScore { get; set; }
    public bool? IsDuplicateCandidate { get; set; }
    public string? RawResponse { get; set; }
    public float[]? Embedding { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public IndexedFile? File { get; set; }
}
