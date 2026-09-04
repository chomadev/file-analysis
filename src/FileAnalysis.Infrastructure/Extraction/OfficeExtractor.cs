using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileAnalysis.Core.Interfaces;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>Extracts text from Office Open XML documents (docx, xlsx, pptx).</summary>
public class OfficeExtractor : IContentExtractor
{
    public IReadOnlyList<string> SupportedMimeTypes { get; } =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    ];

    public Task<string?> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".docx" => Task.FromResult(ExtractDocx(filePath)),
                ".xlsx" => Task.FromResult(ExtractXlsx(filePath)),
                ".pptx" => Task.FromResult(ExtractPptx(filePath)),
                _ => Task.FromResult<string?>(null),
            };
        }
        catch { return Task.FromResult<string?>(null); }
    }

    private static string? ExtractDocx(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
            sb.AppendLine(para.InnerText);
        return sb.ToString().Trim();
    }

    private static string? ExtractXlsx(string filePath)
    {
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbook = doc.WorkbookPart;
        if (workbook is null) return null;
        var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable;
        var sb = new System.Text.StringBuilder();
        foreach (var sheet in workbook.WorksheetParts)
        {
            var rows = sheet.Worksheet?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>() ?? [];
            foreach (var row in rows)
            {
                var cells = row.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>();
                var values = cells.Select(c =>
                {
                    if (c.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
                        && int.TryParse(c.InnerText, out var idx))
                        return sharedStrings?.ElementAt(idx).InnerText ?? string.Empty;
                    return c.InnerText;
                });
                sb.AppendLine(string.Join("\t", values));
            }
        }
        return sb.ToString().Trim();
    }

    private static string? ExtractPptx(string filePath)
    {
        using var doc = PresentationDocument.Open(filePath, false);
        var presentation = doc.PresentationPart;
        if (presentation is null) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var slide in presentation.SlideParts)
        {
            var texts = slide.Slide?.Descendants<DocumentFormat.OpenXml.Drawing.Text>() ?? [];
            sb.AppendLine(string.Join(" ", texts.Select(t => t.Text)));
        }
        return sb.ToString().Trim();
    }
}
