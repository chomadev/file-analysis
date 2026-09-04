using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of IFileRepository.</summary>
public class FileRepository(FileAnalysisDbContext db) : IFileRepository
{
    public async Task<IndexedFile?> GetByPathAsync(string path, CancellationToken ct = default) =>
        await db.Files.Include(f => f.Analysis).FirstOrDefaultAsync(f => f.Path == path, ct);

    public async Task<IndexedFile?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IndexedFile> UpsertAsync(IndexedFile file, CancellationToken ct = default)
    {
        var existing = await db.Files.FirstOrDefaultAsync(f => f.Path == file.Path, ct);
        if (existing is null)
        {
            db.Files.Add(file);
            await db.SaveChangesAsync(ct);
            return file;
        }

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
}
