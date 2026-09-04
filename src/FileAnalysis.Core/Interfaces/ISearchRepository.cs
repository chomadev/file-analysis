using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Read-side queries over the index: hybrid search, similarity, detail, and stats.</summary>
public interface ISearchRepository
{
    /// <summary>Hybrid full-text + semantic search. <paramref name="queryEmbedding"/> may be null (falls back to full-text only).</summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, float[]? queryEmbedding, int limit, SearchFilters filters, CancellationToken ct = default);

    /// <summary>Finds files whose embedding is closest to <paramref name="fileId"/>'s.</summary>
    Task<IReadOnlyList<SearchResult>> FindSimilarAsync(Guid fileId, int limit, CancellationToken ct = default);

    Task<FileDetail?> GetDetailAsync(Guid fileId, CancellationToken ct = default);

    Task<IndexStats> GetStatsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<(string Category, int Count)>> ListCategoriesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListTagsAsync(string? prefix = null, CancellationToken ct = default);
}
