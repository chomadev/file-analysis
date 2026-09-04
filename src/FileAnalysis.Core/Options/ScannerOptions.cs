namespace FileAnalysis.Core.Options;

/// <summary>Configuration options for the directory scanner.</summary>
public class ScannerOptions
{
    public const string SectionName = "Scanner";

    public string[] ExcludePatterns { get; set; } =
        ["node_modules", ".git", "bin", "obj", ".vs", "*.tmp", "*.log"];

    public long MaxFileSizeBytes { get; set; } = 52_428_800; // 50 MB
}
