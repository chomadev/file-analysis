using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of IFileAnalysisRepository.</summary>
public class FileAnalysisRepository(FileAnalysisDbContext db) : IFileAnalysisRepository
{
    public async Task<FileAnalysisResult?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        await db.FileAnalyses.FirstOrDefaultAsync(a => a.FileId == fileId, ct);

    public async Task UpsertAsync(FileAnalysisResult analysis, CancellationToken ct = default)
    {
        var existing = await db.FileAnalyses.FirstOrDefaultAsync(a => a.FileId == analysis.FileId, ct);
        if (existing is null)
        {
            db.FileAnalyses.Add(analysis);
            await db.SaveChangesAsync(ct);
            return;
        }

        existing.Summary = analysis.Summary;
        existing.Category = analysis.Category;
        existing.Language = analysis.Language;
        existing.Tags = analysis.Tags;
        existing.Topics = analysis.Topics;
        existing.UsefulnessScore = analysis.UsefulnessScore;
        existing.IsDuplicateCandidate = analysis.IsDuplicateCandidate;
        existing.RawResponse = analysis.RawResponse;
        existing.Embedding = analysis.Embedding;
        existing.AnalyzedAt = analysis.AnalyzedAt;
        await db.SaveChangesAsync(ct);
    }
}
