using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Infrastructure.Cleanup;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of ICleanupSuggestionRepository. Uses raw SQL for the pgvector
/// near-duplicate probe, matching SearchRepository's pattern.</summary>
public class CleanupSuggestionRepository(FileAnalysisDbContext db) : ICleanupSuggestionRepository
{
    public async Task<IReadOnlyList<(Guid FileId, string Path, long SizeBytes, Guid KeeperFileId)>> FindExactDuplicateGroupsAsync(
        long minSizeBytes, CancellationToken ct = default)
    {
        var rows = await db.Files.AsNoTracking()
            .Where(f => f.Md5Hash != null && f.SizeBytes >= minSizeBytes)
            .Select(f => new
            {
                f.Id,
                f.Path,
                f.SizeBytes,
                f.Md5Hash,
                f.IndexedAt,
                UsefulnessScore = f.Analysis != null ? f.Analysis.UsefulnessScore : null,
            })
            .ToListAsync(ct);

        var ineligibleKeepers = (await db.CleanupSuggestions.AsNoTracking()
            .Where(s => s.Status == CleanupStatus.Approved || s.Status == CleanupStatus.Applied)
            .Select(s => s.FileId)
            .ToListAsync(ct))
            .ToHashSet();

        var result = new List<(Guid, string, long, Guid)>();

        foreach (var group in rows.GroupBy(r => r.Md5Hash))
        {
            if (group.Count() < 2)
                continue;

            var groupList = group.ToList();
            var candidates = groupList.Select(r => new DuplicateCandidate(r.Id, r.Path, r.UsefulnessScore, r.IndexedAt)).ToList();
            var keeperId = DuplicateGroupSelector.SelectKeeper(candidates, ineligibleKeepers);

            foreach (var member in groupList)
            {
                if (member.Id == keeperId)
                    continue;
                result.Add((member.Id, member.Path, member.SizeBytes, keeperId));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<(Guid FileId, Guid CandidateFileId, double Similarity)>> FindNearDuplicatesAsync(
        double threshold, int limitPerFile, CancellationToken ct = default)
    {
        var candidates = await db.FileAnalyses.AsNoTracking()
            .Where(a => a.Embedding != null)
            .Select(a => new { a.FileId, Extension = a.File!.Extension, a.Embedding })
            .ToListAsync(ct);

        var results = new List<(Guid, Guid, double)>();
        var seenPairs = new HashSet<(Guid, Guid)>();

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (c.Embedding is null)
                continue;

            var vector = new Vector(c.Embedding);
            const string sql = """
                SELECT fa2."FileId" AS "CandidateFileId", 1 - (fa2."Embedding" <=> {0}) AS "Similarity"
                FROM file_analyses fa2
                JOIN files f2 ON f2."Id" = fa2."FileId"
                WHERE fa2."FileId" != {1} AND fa2."Embedding" IS NOT NULL AND f2."Extension" = {2}
                ORDER BY fa2."Embedding" <=> {0}
                LIMIT {3}
                """;

            var rows = await db.Database
                .SqlQueryRaw<NearDuplicateRow>(sql, vector, c.FileId, c.Extension ?? "", limitPerFile)
                .ToListAsync(ct);

            foreach (var row in rows.Where(r => r.Similarity >= threshold))
            {
                var pairKey = c.FileId.CompareTo(row.CandidateFileId) < 0
                    ? (c.FileId, row.CandidateFileId)
                    : (row.CandidateFileId, c.FileId);
                if (!seenPairs.Add(pairKey))
                    continue;

                results.Add((c.FileId, row.CandidateFileId, row.Similarity));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<(Guid FileId, string Path, int? UsefulnessScore, DateTime? FileModifiedAt)>> ListCandidateFilesAsync(
        string? pathPrefix, CancellationToken ct = default)
    {
        var query = db.Files.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(pathPrefix))
            query = query.Where(f => f.Path.StartsWith(pathPrefix));

        var rows = await query
            .Select(f => new
            {
                f.Id,
                f.Path,
                UsefulnessScore = f.Analysis != null ? f.Analysis.UsefulnessScore : null,
                f.FileModifiedAt,
            })
            .ToListAsync(ct);

        return rows.Select(r => (r.Id, r.Path, r.UsefulnessScore, r.FileModifiedAt)).ToList();
    }

    public async Task<CleanupSuggestion?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        await db.CleanupSuggestions.FirstOrDefaultAsync(s => s.FileId == fileId, ct);

    public async Task<CleanupSuggestion?> GetByIdOrPrefixAsync(string idOrPrefix, CancellationToken ct = default)
    {
        if (Guid.TryParse(idOrPrefix, out var exactId))
            return await db.CleanupSuggestions.FirstOrDefaultAsync(s => s.Id == exactId, ct);

        var prefix = idOrPrefix.ToLowerInvariant();
        var ids = await db.CleanupSuggestions.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
        var matches = ids.Where(id => id.ToString("N").StartsWith(prefix, StringComparison.Ordinal)).ToList();

        if (matches.Count == 0)
            return null;
        if (matches.Count > 1)
            throw new InvalidOperationException($"ID prefix '{idOrPrefix}' matches {matches.Count} suggestions — use more characters.");

        return await db.CleanupSuggestions.FirstOrDefaultAsync(s => s.Id == matches[0], ct);
    }

    public async Task UpsertAsync(CleanupSuggestion suggestion, CancellationToken ct = default)
    {
        var existing = await db.CleanupSuggestions.FirstOrDefaultAsync(s => s.FileId == suggestion.FileId, ct);
        if (existing is null)
        {
            db.CleanupSuggestions.Add(suggestion);
            await db.SaveChangesAsync(ct);
            return;
        }

        existing.Status = suggestion.Status;
        existing.Reason = suggestion.Reason;
        existing.Details = suggestion.Details;
        existing.DuplicateOfFileId = suggestion.DuplicateOfFileId;
        existing.DecidedAt = suggestion.DecidedAt;
        existing.AppliedAt = suggestion.AppliedAt;
        existing.QuarantinePath = suggestion.QuarantinePath;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CleanupSuggestion>> ListAsync(CleanupStatus? status, string? pathPrefix, CancellationToken ct = default)
    {
        var query = db.CleanupSuggestions.AsNoTracking().Include(s => s.File).AsQueryable();
        if (status is not null)
            query = query.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(pathPrefix))
            query = query.Where(s => s.File!.Path.StartsWith(pathPrefix));

        return await query.OrderBy(s => s.SuggestedAt).ToListAsync(ct);
    }

    public async Task<int> BulkSetStatusAsync(CleanupStatus from, CleanupStatus to, CleanupReason? reasonFilter, string? pathPrefix, CancellationToken ct = default)
    {
        var query = db.CleanupSuggestions.Where(s => s.Status == from);
        if (reasonFilter is not null)
            query = query.Where(s => s.Reason == reasonFilter);
        if (!string.IsNullOrWhiteSpace(pathPrefix))
            query = query.Where(s => s.File!.Path.StartsWith(pathPrefix));

        var items = await query.ToListAsync(ct);
        foreach (var item in items)
        {
            item.Status = to;
            item.DecidedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return items.Count;
    }

    private sealed class NearDuplicateRow
    {
        public Guid CandidateFileId { get; set; }
        public double Similarity { get; set; }
    }
}
