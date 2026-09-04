using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Persistence operations for indexed files.</summary>
public interface IFileRepository
{
    Task<IndexedFile?> GetByPathAsync(string path, CancellationToken ct = default);
    Task<IndexedFile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IndexedFile> UpsertAsync(IndexedFile file, CancellationToken ct = default);
    Task<IReadOnlyList<IndexedFile>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Bulk-preloads scan-relevant metadata for every file whose Path starts with
    /// <paramref name="rootPathPrefix"/>, keyed by Path with ordinal comparison, in one AsNoTracking
    /// projection query — used by FileScanner instead of a per-file GetByPathAsync round trip. Deliberately
    /// not a tracked entity fetch: tracking every IndexedFile under a large scan root would make each
    /// per-file UpsertAsync's SaveChanges go quadratic via DetectChanges().</summary>
    Task<IReadOnlyDictionary<string, FileScanMetadata>> GetScanMetadataAsync(string rootPathPrefix, CancellationToken ct = default);
}
