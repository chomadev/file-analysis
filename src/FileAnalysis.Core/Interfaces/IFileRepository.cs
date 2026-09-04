using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Persistence operations for indexed files.</summary>
public interface IFileRepository
{
    Task<IndexedFile?> GetByPathAsync(string path, CancellationToken ct = default);
    Task<IndexedFile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IndexedFile> UpsertAsync(IndexedFile file, CancellationToken ct = default);
    Task<IReadOnlyList<IndexedFile>> GetAllAsync(CancellationToken ct = default);
}
