using FileAnalysis.Core.Interfaces;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>Reads source code files as plain text.</summary>
public class CodeFileExtractor : IContentExtractor
{
    public IReadOnlyList<string> SupportedMimeTypes { get; } =
    [
        "text/x-csharp", "text/javascript", "text/typescript",
        "text/x-python", "text/x-go", "text/x-rust", "text/x-java",
        "text/x-c++", "text/x-c", "text/x-sh", "text/x-bat",
        "text/x-powershell", "text/x-sql",
    ];

    public async Task<string?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        try { return await File.ReadAllTextAsync(filePath, ct); }
        catch { return null; }
    }
}
