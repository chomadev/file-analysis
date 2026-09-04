namespace FileAnalysis.Core.Models;

/// <summary>Scan-relevant metadata for a previously-indexed file, as bulk-preloaded by FileScanner before
/// a scan run — enough to decide "unchanged" without a per-file DB round trip.</summary>
public sealed record FileScanMetadata(
    Guid Id,
    string? Md5Hash,
    long SizeBytes,
    DateTime? FileModifiedAt,
    bool HasChunks,
    bool HasAnalysis);
