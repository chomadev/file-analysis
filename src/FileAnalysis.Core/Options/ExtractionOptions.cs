namespace FileAnalysis.Core.Options;

/// <summary>Configuration for content extraction and chunking.</summary>
public class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>Max characters per chunk.</summary>
    public int ChunkSize { get; set; } = 4_000;

    /// <summary>Overlap between consecutive chunks in characters.</summary>
    public int ChunkOverlap { get; set; } = 200;

    /// <summary>Files smaller than this are not chunked (stored as a single chunk).</summary>
    public int SingleChunkThresholdBytes { get; set; } = 51_200; // 50 KB
}
