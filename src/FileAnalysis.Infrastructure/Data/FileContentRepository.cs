using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of IFileContentRepository.</summary>
public class FileContentRepository(FileAnalysisDbContext db) : IFileContentRepository
{
    public async Task SaveChunksAsync(Guid fileId, IReadOnlyList<string> chunks, CancellationToken ct = default)
    {
        await DeleteChunksAsync(fileId, ct);
        for (var i = 0; i < chunks.Count; i++)
        {
            db.FileContents.Add(new FileContent
            {
                FileId = fileId,
                ChunkIndex = i,
                Content = chunks[i],
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FileContent>> GetChunksAsync(Guid fileId, CancellationToken ct = default) =>
        await db.FileContents
            .Where(c => c.FileId == fileId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

    public async Task DeleteChunksAsync(Guid fileId, CancellationToken ct = default)
    {
        await db.FileContents.Where(c => c.FileId == fileId).ExecuteDeleteAsync(ct);
    }
}
