namespace FileAnalysis.Core.Models;

/// <summary>Represents a file tracked by the index.</summary>
public class IndexedFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Path { get; set; }
    public required string Name { get; set; }
    public string? Extension { get; set; }
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }
    public string? Md5Hash { get; set; }
    public DateTime? FileCreatedAt { get; set; }
    public DateTime? FileModifiedAt { get; set; }
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastScannedAt { get; set; }

    public ICollection<FileContent> Contents { get; set; } = [];
    public FileAnalysisResult? Analysis { get; set; }
}
