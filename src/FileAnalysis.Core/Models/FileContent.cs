namespace FileAnalysis.Core.Models;

/// <summary>Extracted text content from a file (populated in Phase 2).</summary>
public class FileContent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public int ChunkIndex { get; set; } = 0;
    public required string Content { get; set; }
    public IndexedFile? File { get; set; }
}
