namespace FileAnalysis.Core.Models;

/// <summary>A cheap, pre-indexing survey of one directory's structure — used to decide whether its
/// contents are worth indexing at all before the expensive per-file pipeline runs.</summary>
public class DirectorySurveyEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Path { get; set; }
    public required string ScanRootPath { get; set; }
    public int Depth { get; set; }
    public int DirectFileCount { get; set; }
    public long DirectSizeBytes { get; set; }

    /// <summary>Recursive totals, including everything below <see cref="Depth"/>'s survey cutoff.</summary>
    public int TotalFileCount { get; set; }
    public long TotalSizeBytes { get; set; }

    public string[] TopExtensions { get; set; } = [];

    public DirectoryClassification Classification { get; set; } = DirectoryClassification.Unclassified;
    public ClassificationSource ClassificationSource { get; set; } = ClassificationSource.None;
    public string? ClassificationReason { get; set; }

    /// <summary>Null = undecided, surfaced for human confirmation. True = excluded from indexing.</summary>
    public bool? ExcludeDecision { get; set; }

    public DateTime SurveyedAt { get; set; } = DateTime.UtcNow;
}

public enum DirectoryClassification { Unclassified, LikelyJunk, LikelyContent, Ambiguous }

public enum ClassificationSource { None, Heuristic, Ai, Human }
