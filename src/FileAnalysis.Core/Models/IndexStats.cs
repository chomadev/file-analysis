namespace FileAnalysis.Core.Models;

/// <summary>Aggregate overview of the index.</summary>
public record IndexStats(
    int TotalFiles,
    long TotalSizeBytes,
    int AnalyzedFiles,
    int PendingAnalysis,
    IReadOnlyDictionary<string, int> ByExtension,
    IReadOnlyDictionary<string, int> ByCategory);
