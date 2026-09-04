using FileAnalysis.Core.Interfaces;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>Routes extraction requests to the appropriate extractor by MIME type.</summary>
public class ContentExtractorDispatcher(IEnumerable<IContentExtractor> extractors)
{
    private readonly Dictionary<string, IContentExtractor> _map =
        extractors.SelectMany(e => e.SupportedMimeTypes.Select(m => (mime: m, extractor: e)))
                  .ToDictionary(x => x.mime, x => x.extractor, StringComparer.OrdinalIgnoreCase);

    /// <summary>Extracts content from <paramref name="filePath"/> using the extractor for <paramref name="mimeType"/>.</summary>
    public Task<string?> ExtractAsync(string filePath, string mimeType, CancellationToken ct = default)
    {
        // Also try prefix match for text/* types not explicitly registered
        if (!_map.TryGetValue(mimeType, out var extractor))
        {
            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                extractor = _map.GetValueOrDefault("text/plain");
        }
        return extractor is not null
            ? extractor.ExtractAsync(filePath, ct)
            : Task.FromResult<string?>(null);
    }
}
