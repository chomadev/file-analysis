using System.ComponentModel;
using System.Text.Json;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Infrastructure.Analysis;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FileAnalysis.MCP.Tools;

/// <summary>
/// MCP tools exposing the file index. Each tool creates its own DI scope via <see cref="IServiceScopeFactory"/>
/// rather than relying on scoped parameter injection, since scoped-service handling varies across MCP transports.
/// </summary>
[McpServerToolType]
public sealed class FileSearchTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description("Search the local file index using hybrid full-text and semantic (embedding) search. Returns ranked matches with summary, category, and tags.")]
    public static async Task<string> SearchFiles(
        IServiceScopeFactory scopeFactory,
        [Description("Search query text")] string query,
        [Description("Maximum number of results to return")] int limit = 10,
        [Description("Filter by file extension, e.g. 'pdf' or '.pdf'")] string? type = null,
        [Description("Filter by tag")] string? tag = null,
        [Description("Filter by category")] string? category = null,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var searchService = scope.ServiceProvider.GetRequiredService<SearchService>();

        var filters = new SearchFilters
        {
            Extension = string.IsNullOrWhiteSpace(type) ? null : "." + type.TrimStart('.').ToLowerInvariant(),
            Tag = tag,
            Category = category,
        };

        var results = await searchService.SearchAsync(query, limit, filters, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool, Description("Get the full AI analysis and metadata for a single indexed file by its file ID.")]
    public static async Task<string> GetFileAnalysis(
        IServiceScopeFactory scopeFactory,
        [Description("File ID, as returned by search_files or find_similar")] Guid fileId,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();

        var detail = await repo.GetDetailAsync(fileId, ct);
        if (detail is null)
            return JsonSerializer.Serialize(new { error = "file not found" }, JsonOptions);

        return JsonSerializer.Serialize(new
        {
            fileId = detail.File.Id,
            detail.File.Path,
            detail.File.Name,
            detail.File.Extension,
            detail.File.MimeType,
            detail.File.SizeBytes,
            detail.File.IndexedAt,
            detail.File.LastScannedAt,
            analysis = detail.Analysis is { } a
                ? new
                {
                    a.Summary,
                    a.Category,
                    a.Language,
                    a.Tags,
                    a.Topics,
                    a.UsefulnessScore,
                    a.IsDuplicateCandidate,
                    a.AnalyzedAt,
                }
                : null,
            contentChunkCount = detail.Contents.Count,
        }, JsonOptions);
    }

    [McpServerTool, Description("Find files whose AI-generated embedding is semantically closest to a given file's embedding.")]
    public static async Task<string> FindSimilar(
        IServiceScopeFactory scopeFactory,
        [Description("File ID to find similar files for")] Guid fileId,
        [Description("Maximum number of results to return")] int limit = 10,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();

        var results = await repo.FindSimilarAsync(fileId, limit, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool, Description("List all categories present in the index along with how many files fall into each.")]
    public static async Task<string> ListCategories(IServiceScopeFactory scopeFactory, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();

        var categories = await repo.ListCategoriesAsync(ct);
        return JsonSerializer.Serialize(categories.Select(c => new { c.Category, c.Count }), JsonOptions);
    }

    [McpServerTool, Description("List distinct tags across the index, optionally filtered by a prefix.")]
    public static async Task<string> ListTags(
        IServiceScopeFactory scopeFactory,
        [Description("Optional prefix to filter tags by")] string? prefix = null,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();

        var tags = await repo.ListTagsAsync(prefix, ct);
        return JsonSerializer.Serialize(tags, JsonOptions);
    }

    [McpServerTool, Description("Get overall index statistics: total files, total size, how many are analyzed vs. pending, and breakdowns by extension and category.")]
    public static async Task<string> GetStats(IServiceScopeFactory scopeFactory, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISearchRepository>();

        var stats = await repo.GetStatsAsync(ct);
        return JsonSerializer.Serialize(stats, JsonOptions);
    }
}
