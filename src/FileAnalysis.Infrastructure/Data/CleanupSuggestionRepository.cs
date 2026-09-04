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

    // NOTE ON A REVERTED OPTIMIZATION: this was rewritten to a single CROSS JOIN LATERAL query (one round
    // trip for the whole table instead of one SqlQueryRaw per embedded file) and then reverted back to the
    // per-file loop below after EXPLAIN ANALYZE against the real dev DB (14,668 files / 1,442 embeddings)
    // showed the lateral subplan does NOT use idx_analyses_embedding — it falls back to a Seq Scan +
    // Hash Join + top-N sort, same as this per-file form does today. The index is only reachable by the
    // planner when the ORDER BY ... LIMIT sits directly on file_analyses; joining in files for the
    // f2."Extension" = f1."Extension" filter (in either the lateral or this per-file form) defeats it. A
    // server-side loop of 1,442 individual per-file statements (same shape as below, no client round
    // trips) measured ~290ms total; the lateral form measured ~2.4-2.7s for the same result set — i.e. the
    // lateral rewrite was not a win here even ignoring the extra round trips it was meant to save. Revisit
    // if the extension filter is ever denormalized onto file_analyses itself (would let both the lateral
    // and this form use the index), per the task's "do not ship a change that's silently worse" gate.
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
        string? pathPrefix, int minUsefulness, DateTime staleCutoff, CancellationToken ct = default)
    {
        var query = db.Files.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(pathPrefix))
            query = query.Where(f => f.Path.StartsWith(pathPrefix));

        // Push the LowUsefulness/Stale threshold check into the query so we only ever pull rows that can
        // actually produce a suggestion, instead of loading every indexed file and filtering in C#.
        query = query.Where(f =>
            (f.Analysis != null && f.Analysis.UsefulnessScore <= minUsefulness) ||
            (f.FileModifiedAt != null && f.FileModifiedAt < staleCutoff));

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

    public async Task<IReadOnlyDictionary<Guid, string>> GetPathsByIdsAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        if (fileIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await db.Files.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Path })
            .ToDictionaryAsync(f => f.Id, f => f.Path, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, CleanupStatus>> GetStatusesByFileIdsAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        if (fileIds.Count == 0)
            return new Dictionary<Guid, CleanupStatus>();

        return await db.CleanupSuggestions.AsNoTracking()
            .Where(s => fileIds.Contains(s.FileId))
            .Select(s => new { s.FileId, s.Status })
            .ToDictionaryAsync(s => s.FileId, s => s.Status, ct);
    }

    public async Task<CleanupSuggestion?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        await db.CleanupSuggestions.AsNoTracking().FirstOrDefaultAsync(s => s.FileId == fileId, ct);

    public async Task<CleanupSuggestion?> GetByIdOrPrefixAsync(string idOrPrefix, CancellationToken ct = default)
    {
        if (Guid.TryParse(idOrPrefix, out var exactId))
            return await db.CleanupSuggestions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == exactId, ct);

        var prefix = idOrPrefix.ToLowerInvariant();

        // Server-side prefix match against the undashed hex form — what's actually shown to users
        // (Id.ToString("N")[..8]). The old in-memory scan loaded every suggestion ID up front and,
        // being a plain StartsWith against ToString("N"), matched fine for that display case; this
        // does the same match in SQL so it never has to materialize every row, and works for any
        // prefix length instead of relying on client-side string handling.
        const string sql = """
            SELECT "Id" FROM cleanup_suggestions WHERE REPLACE("Id"::text, '-', '') LIKE {0}
            """;
        var matches = await db.Database
            .SqlQueryRaw<Guid>(sql, prefix + "%")
            .ToListAsync(ct);

        if (matches.Count == 0)
            return null;
        if (matches.Count > 1)
            throw new InvalidOperationException($"ID prefix '{idOrPrefix}' matches {matches.Count} suggestions — use more characters.");

        return await db.CleanupSuggestions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == matches[0], ct);
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
