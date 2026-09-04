using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of IDirectorySurveyRepository.</summary>
public class DirectorySurveyRepository(FileAnalysisDbContext db) : IDirectorySurveyRepository
{
    public async Task UpsertAsync(DirectorySurveyEntry entry, CancellationToken ct = default)
    {
        var existing = await db.DirectorySurveyEntries.FirstOrDefaultAsync(e => e.Path == entry.Path, ct);
        if (existing is null)
        {
            db.DirectorySurveyEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return;
        }

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

    public async Task<DirectorySurveyEntry?> GetByPathAsync(string path, CancellationToken ct = default) =>
        await db.DirectorySurveyEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Path == path, ct);

    public async Task<IReadOnlyList<DirectorySurveyEntry>> ListByRootAsync(string scanRootPath, CancellationToken ct = default) =>
        await db.DirectorySurveyEntries.AsNoTracking()
            .Where(e => e.ScanRootPath == scanRootPath)
            .OrderBy(e => e.Path)
            .ToListAsync(ct);

    public async Task<IReadOnlySet<string>> GetExcludedPathsAsync(string scanRootPath, CancellationToken ct = default)
    {
        var paths = await db.DirectorySurveyEntries.AsNoTracking()
            .Where(e => e.ScanRootPath == scanRootPath && e.ExcludeDecision == true)
            .Select(e => e.Path)
            .ToListAsync(ct);
        return paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
