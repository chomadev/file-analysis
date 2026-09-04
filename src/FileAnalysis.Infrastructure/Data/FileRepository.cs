using FileAnalysis.Core;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of IFileRepository.</summary>
public class FileRepository(FileAnalysisDbContext db) : IFileRepository
{
    public async Task<IndexedFile?> GetByPathAsync(string path, CancellationToken ct = default)
    {
        if (PathComparison.IsCaseInsensitive)
        {
            var lower = path.ToLowerInvariant();
            return await db.Files.Include(f => f.Analysis).FirstOrDefaultAsync(f => f.Path.ToLower() == lower, ct);
        }
        return await db.Files.Include(f => f.Analysis).FirstOrDefaultAsync(f => f.Path == path, ct);
    }

    public async Task<IndexedFile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    // The existence check here is what actually prevents a duplicate row on a rescan whose root path is
    // cased differently from a prior run (e.g. "C:\Code\X" vs "c:\code\x") — see PathComparison's doc
    // comment. Matching case-insensitively on Windows/macOS, and refreshing existing.Path to the
    // most-recently-observed casing, keeps the stored path in sync without ever inserting a second row for
    // the same physical file.
    public async Task<IndexedFile> UpsertAsync(IndexedFile file, CancellationToken ct = default)
    {
        IndexedFile? existing;
        if (PathComparison.IsCaseInsensitive)
        {
            var lower = file.Path.ToLowerInvariant();
            existing = await db.Files.FirstOrDefaultAsync(f => f.Path.ToLower() == lower, ct);
        }
        else
        {
            existing = await db.Files.FirstOrDefaultAsync(f => f.Path == file.Path, ct);
        }

        if (existing is null)
        {
            db.Files.Add(file);
            await db.SaveChangesAsync(ct);
            return file;
        }

        existing.Path = file.Path;
        existing.Name = file.Name;
        existing.Extension = file.Extension;
        existing.MimeType = file.MimeType;
        existing.SizeBytes = file.SizeBytes;
        existing.Md5Hash = file.Md5Hash;
        existing.FileCreatedAt = file.FileCreatedAt;
        existing.FileModifiedAt = file.FileModifiedAt;
        existing.LastScannedAt = file.LastScannedAt;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<IReadOnlyList<IndexedFile>> GetAllAsync(CancellationToken ct = default) =>
        await db.Files.ToListAsync(ct);

    public async Task<IReadOnlyDictionary<string, FileScanMetadata>> GetScanMetadataAsync(string rootPathPrefix, CancellationToken ct = default)
    {
        var query = db.Files.AsNoTracking().AsQueryable();
        query = PathComparison.IsCaseInsensitive
            ? query.Where(f => f.Path.ToLower().StartsWith(rootPathPrefix.ToLower()))
            : query.Where(f => f.Path.StartsWith(rootPathPrefix));

        var rows = await query
            .Select(f => new
            {
                f.Path,
                f.Id,
                f.Md5Hash,
                f.SizeBytes,
                f.FileModifiedAt,
                HasChunks = f.Contents.Any(),
                HasAnalysis = f.Analysis != null,
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Path,
            r => new FileScanMetadata(r.Id, r.Md5Hash, r.SizeBytes, r.FileModifiedAt, r.HasChunks, r.HasAnalysis),
            PathComparison.Comparer);
    }
}
