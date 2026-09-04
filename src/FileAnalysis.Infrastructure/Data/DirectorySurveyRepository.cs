using FileAnalysis.Core;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of IDirectorySurveyRepository.</summary>
public class DirectorySurveyRepository(FileAnalysisDbContext db) : IDirectorySurveyRepository
{
    // Case-insensitive existence check on Windows/macOS (see PathComparison) — otherwise a directory
    // surveyed under a differently-cased root on a later run would look "new" and get a second row instead
    // of updating the one already holding its exclude decision.
    public async Task UpsertAsync(DirectorySurveyEntry entry, CancellationToken ct = default)
    {
        DirectorySurveyEntry? existing;
        if (PathComparison.IsCaseInsensitive)
        {
            var lower = entry.Path.ToLowerInvariant();
            existing = await db.DirectorySurveyEntries.FirstOrDefaultAsync(e => e.Path.ToLower() == lower, ct);
        }
        else
        {
            existing = await db.DirectorySurveyEntries.FirstOrDefaultAsync(e => e.Path == entry.Path, ct);
        }

        if (existing is null)
        {
            db.DirectorySurveyEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return;
        }

        existing.Path = entry.Path;
        existing.ScanRootPath = entry.ScanRootPath;
        existing.Depth = entry.Depth;
        existing.DirectFileCount = entry.DirectFileCount;
        existing.DirectSizeBytes = entry.DirectSizeBytes;
        existing.TotalFileCount = entry.TotalFileCount;
        existing.TotalSizeBytes = entry.TotalSizeBytes;
        existing.TopExtensions = entry.TopExtensions;
        existing.Classification = entry.Classification;
        existing.ClassificationSource = entry.ClassificationSource;
        existing.ClassificationReason = entry.ClassificationReason;
        existing.ExcludeDecision = entry.ExcludeDecision;
        existing.SurveyedAt = entry.SurveyedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<DirectorySurveyEntry?> GetByPathAsync(string path, CancellationToken ct = default)
    {
        if (PathComparison.IsCaseInsensitive)
        {
            var lower = path.ToLowerInvariant();
            return await db.DirectorySurveyEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Path.ToLower() == lower, ct);
        }
        return await db.DirectorySurveyEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Path == path, ct);
    }

    public async Task<IReadOnlyList<DirectorySurveyEntry>> ListByRootAsync(string scanRootPath, CancellationToken ct = default)
    {
        var query = db.DirectorySurveyEntries.AsNoTracking().AsQueryable();
        query = PathComparison.IsCaseInsensitive
            ? query.Where(e => e.ScanRootPath.ToLower() == scanRootPath.ToLower())
            : query.Where(e => e.ScanRootPath == scanRootPath);
        return await query.OrderBy(e => e.Path).ToListAsync(ct);
    }

    public async Task<IReadOnlySet<string>> GetExcludedPathsAsync(string scanRootPath, CancellationToken ct = default)
    {
        var query = db.DirectorySurveyEntries.AsNoTracking().Where(e => e.ExcludeDecision == true).AsQueryable();
        query = PathComparison.IsCaseInsensitive
            ? query.Where(e => e.ScanRootPath.ToLower() == scanRootPath.ToLower())
            : query.Where(e => e.ScanRootPath == scanRootPath);

        var paths = await query.Select(e => e.Path).ToListAsync(ct);
        return paths.ToHashSet(PathComparison.Comparer);
    }
}
