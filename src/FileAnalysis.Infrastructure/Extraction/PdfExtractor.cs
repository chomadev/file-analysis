using FileAnalysis.Core.Interfaces;
using UglyToad.PdfPig;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>Extracts text from PDF files using PdfPig.</summary>
public class PdfExtractor : IContentExtractor
{
    public IReadOnlyList<string> SupportedMimeTypes { get; } = ["application/pdf"];

    public Task<string?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var doc = PdfDocument.Open(filePath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in doc.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                sb.AppendLine(page.Text);
            }
            return Task.FromResult<string?>(sb.ToString().Trim());
        }
        catch { return Task.FromResult<string?>(null); }
    }
}
