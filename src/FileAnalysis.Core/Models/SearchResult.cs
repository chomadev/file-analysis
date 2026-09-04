namespace FileAnalysis.Core.Models;

/// <summary>A single ranked search hit.</summary>
public record SearchResult(
    Guid FileId,
    string Path,
    string Name,
    string? Extension,
    string? Category,
    string? Summary,
    string[] Tags,
    int? UsefulnessScore,
    double Score);
