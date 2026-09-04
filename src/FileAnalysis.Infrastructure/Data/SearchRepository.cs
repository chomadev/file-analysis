using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace FileAnalysis.Infrastructure.Data;

/// <summary>EF Core implementation of ISearchRepository. Uses raw SQL for hybrid full-text + pgvector ranking.</summary>
public class SearchRepository(FileAnalysisDbContext db) : ISearchRepository
{
    private const string SelectColumns = """
        fa."FileId" AS "FileId", f."Path" AS "Path", f."Name" AS "Name", f."Extension" AS "Extension",
        fa."Category" AS "Category", fa."Summary" AS "Summary", fa."Tags" AS "Tags", fa."UsefulnessScore" AS "UsefulnessScore"
        """;

    private const string TsVectorExpr = """file_analyses_search_vector(fa."Summary", fa."Tags")""";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, float[]? queryEmbedding, int limit, SearchFilters filters, CancellationToken ct = default)
    {
        var parameters = new List<object>();
        string P(object value) { parameters.Add(value); return "{" + (parameters.Count - 1) + "}"; }

        var queryParam = P(query);
        var vectorParam = queryEmbedding is { } emb ? P(new Vector(emb)) : null;

        var scoreExpr = vectorParam is not null
            ? $"""(0.5 * COALESCE(1 - (fa."Embedding" <=> {vectorParam}), 0) + 0.5 * COALESCE(ts_rank_cd({TsVectorExpr}, plainto_tsquery('english', {queryParam})), 0))"""
            : $"""COALESCE(ts_rank_cd({TsVectorExpr}, plainto_tsquery('english', {queryParam})), 0)""";

        var conditions = new List<string>
        {
            vectorParam is not null
                ? $"""(fa."Embedding" IS NOT NULL OR {TsVectorExpr} @@ plainto_tsquery('english', {queryParam}))"""
                : $"""{TsVectorExpr} @@ plainto_tsquery('english', {queryParam})""",
        };

        if (filters.Extension is { Length: > 0 } ext) conditions.Add($"""f."Extension" = {P(ext)}""");
        if (filters.Category is { Length: > 0 } cat) conditions.Add($"""fa."Category" = {P(cat)}""");
        if (filters.Tag is { Length: > 0 } tag) conditions.Add($"""fa."Tags" @> ARRAY[{P(tag)}]::text[]""");
        if (filters.Since is { } since) conditions.Add($"""f."FileModifiedAt" >= {P(since)}""");

        var limitParam = P(limit);

        var sql = $"""
            SELECT {SelectColumns}, {scoreExpr} AS "Score"
            FROM file_analyses fa
            JOIN files f ON f."Id" = fa."FileId"
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY "Score" DESC
            LIMIT {limitParam}
            """;

        var rows = await db.Database.SqlQueryRaw<SearchResultRow>(sql, parameters.ToArray()).ToListAsync(ct);
        return rows.Select(MapRow).ToList();
    }

    public async Task<IReadOnlyList<SearchResult>> FindSimilarAsync(Guid fileId, int limit, CancellationToken ct = default)
    {
        var targetEmbedding = await db.FileAnalyses.AsNoTracking()
            .Where(a => a.FileId == fileId)
            .Select(a => a.Embedding)
            .FirstOrDefaultAsync(ct);

        if (targetEmbedding is null)
            return [];

        var vector = new Vector(targetEmbedding);
        var sql = "SELECT " + SelectColumns + """
            , 1 - (fa."Embedding" <=> {0}) AS "Score"
            FROM file_analyses fa
            JOIN files f ON f."Id" = fa."FileId"
            WHERE fa."FileId" != {1} AND fa."Embedding" IS NOT NULL
            ORDER BY fa."Embedding" <=> {0}
            LIMIT {2}
            """;

        var rows = await db.Database.SqlQueryRaw<SearchResultRow>(sql, vector, fileId, limit).ToListAsync(ct);
        return rows.Select(MapRow).ToList();
    }

    public async Task<FileDetail?> GetDetailAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await db.Files.AsNoTracking()
            .Include(f => f.Analysis)
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
            return null;

        var contents = await db.FileContents.AsNoTracking()
            .Where(c => c.FileId == fileId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        return new FileDetail(file, file.Analysis, contents);
    }

    public async Task<IndexStats> GetStatsAsync(CancellationToken ct = default)
    {
        var totalFiles = await db.Files.CountAsync(ct);
        var totalSize = await db.Files.SumAsync(f => (long?)f.SizeBytes, ct) ?? 0;
        var analyzed = await db.FileAnalyses.CountAsync(ct);

        var byExtension = await db.Files.AsNoTracking()
            .GroupBy(f => f.Extension ?? "")
            .Select(g => new { Extension = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byCategory = await db.FileAnalyses.AsNoTracking()
            .Where(a => a.Category != null)
            .GroupBy(a => a.Category!)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new IndexStats(
            totalFiles,
            totalSize,
            analyzed,
            totalFiles - analyzed,
            byExtension.ToDictionary(x => x.Extension, x => x.Count),
            byCategory.ToDictionary(x => x.Category, x => x.Count));
    }

    public async Task<IReadOnlyList<(string Category, int Count)>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var rows = await db.FileAnalyses.AsNoTracking()
            .Where(a => a.Category != null)
            .GroupBy(a => a.Category!)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync(ct);

        return rows.Select(r => (r.Category, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<string>> ListTagsAsync(string? prefix = null, CancellationToken ct = default)
    {
        // Distinct-tag computation pushed into SQL via unnest() instead of loading every analyzed file's
        // tag array and de-duplicating client-side. Tag ordering now follows DB collation rather than
        // StringComparer.OrdinalIgnoreCase — acceptable since tags are LLM-generated and already instructed
        // to be lowercase. DISTINCT is applied to lower(tag), not the raw value: the old
        // .Distinct(StringComparer.OrdinalIgnoreCase) collapsed case-only variants (confirmed present in
        // this index, e.g. "readme"/"README") — a byte-exact SELECT DISTINCT would silently start listing
        // both instead of one, so we normalize case here rather than resurrect that as a bug.
        // unnest() as a FROM-clause set-returning function, one row per tag, so the prefix filter below
        // applies per-tag rather than per-file.
        if (string.IsNullOrWhiteSpace(prefix))
        {
            const string sql = """
                SELECT DISTINCT lower(t.tag) AS "Value"
                FROM file_analyses fa, unnest(fa."Tags") AS t(tag)
                ORDER BY lower(t.tag)
                """;
            return await db.Database.SqlQueryRaw<string>(sql).ToListAsync(ct);
        }

        // prefix is user input (CLI/MCP list_tags) — escape LIKE metacharacters before building the pattern.
        var escapedPrefix = prefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        const string prefixSql = """
            SELECT DISTINCT lower(t.tag) AS "Value"
            FROM file_analyses fa, unnest(fa."Tags") AS t(tag)
            WHERE t.tag ILIKE {0} ESCAPE '\'
            ORDER BY lower(t.tag)
            """;
        return await db.Database.SqlQueryRaw<string>(prefixSql, escapedPrefix + "%").ToListAsync(ct);
    }

    private static SearchResult MapRow(SearchResultRow r) =>
        new(r.FileId, r.Path, r.Name, r.Extension, r.Category, r.Summary, r.Tags ?? [], r.UsefulnessScore, r.Score);

    private sealed class SearchResultRow
    {
        public Guid FileId { get; set; }
        public required string Path { get; set; }
        public required string Name { get; set; }
        public string? Extension { get; set; }
        public string? Category { get; set; }
        public string? Summary { get; set; }
        public string[]? Tags { get; set; }
        public int? UsefulnessScore { get; set; }
        public double Score { get; set; }
    }
}
