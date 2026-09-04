namespace FileAnalysis.Core.Interfaces;

/// <summary>Extracts readable text from a file.</summary>
public interface IContentExtractor
{
    /// <summary>MIME types this extractor handles.</summary>
    IReadOnlyList<string> SupportedMimeTypes { get; }

    /// <summary>Extracts text content. Returns null if the file cannot be read.</summary>
    Task<string?> ExtractAsync(string filePath, CancellationToken ct = default);
}
