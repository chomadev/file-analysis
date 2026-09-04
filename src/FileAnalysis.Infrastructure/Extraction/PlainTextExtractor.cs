using FileAnalysis.Core.Interfaces;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>Reads plain text files directly.</summary>
public class PlainTextExtractor : IContentExtractor
{
    public IReadOnlyList<string> SupportedMimeTypes { get; } =
    [
        "text/plain", "text/markdown", "text/csv", "application/json",
        "application/xml", "application/yaml", "application/toml",
        "text/html", "text/css", "image/svg+xml",
    ];

    public async Task<string?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        try { return await File.ReadAllTextAsync(filePath, ct); }
        catch { return null; }
    }
}
