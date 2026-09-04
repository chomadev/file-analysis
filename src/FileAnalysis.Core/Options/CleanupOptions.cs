namespace FileAnalysis.Core.Options;

/// <summary>Configuration for cleanup-suggestion detection and the apply/quarantine step.</summary>
public class CleanupOptions
{
    public const string SectionName = "Cleanup";

    /// <summary>Directory approved files are moved into (never deleted) by `apply-cleanup`.</summary>
    public string QuarantineRoot { get; set; } = "./quarantine";

    /// <summary>Cosine similarity (0-1) above which two same-extension files are considered near-duplicates.</summary>
    public double NearDuplicateThreshold { get; set; } = 0.97;

    /// <summary>Files smaller than this are excluded from exact-duplicate detection (avoids flagging empty/boilerplate files).</summary>
    public long MinDuplicateSizeBytes { get; set; } = 1024;

    public int MinUsefulnessThreshold { get; set; } = 3;

    public int StaleDays { get; set; } = 365;

    /// <summary>Safety cap on how many suggestions a single generation run can produce, especially over MCP.</summary>
    public int MaxCandidatesPerRun { get; set; } = 5000;
}
