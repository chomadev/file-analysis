using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Core.Options;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Analysis;

/// <summary>Orchestrates hybrid search: embeds the query text (best-effort) then delegates to ISearchRepository.</summary>
public class SearchService(ISearchRepository searchRepo, IOllamaClient ollama, IOptions<OllamaOptions> options)
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int limit, SearchFilters filters, CancellationToken ct = default)
    {
        float[]? embedding = null;
        try
        {
            embedding = await ollama.EmbedAsync(_options.EmbeddingModel, query, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[search] embedding unavailable, falling back to full-text only — {ex.Message}");
        }

        return await searchRepo.SearchAsync(query, embedding, limit, filters, ct);
    }
}
