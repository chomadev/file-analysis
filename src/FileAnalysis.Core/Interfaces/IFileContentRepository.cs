using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Persistence for extracted file content chunks.</summary>
public interface IFileContentRepository
{
    Task SaveChunksAsync(Guid fileId, IReadOnlyList<string> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<FileContent>> GetChunksAsync(Guid fileId, CancellationToken ct = default);
    Task DeleteChunksAsync(Guid fileId, CancellationToken ct = default);
}
