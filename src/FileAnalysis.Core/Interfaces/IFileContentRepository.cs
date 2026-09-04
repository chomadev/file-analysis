using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Persistence for extracted file content chunks.</summary>
public interface IFileContentRepository
{
    Task SaveChunksAsync(Guid fileId, IReadOnlyList<string> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<FileContent>> GetChunksAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Streams chunks ordered by ChunkIndex without materializing the full list — lets a caller
    /// stop reading (e.g. once it has enough content for a prompt) without fetching/joining chunks it
    /// would just discard.</summary>
    IAsyncEnumerable<FileContent> StreamChunksAsync(Guid fileId, CancellationToken ct = default);

    Task DeleteChunksAsync(Guid fileId, CancellationToken ct = default);
}
