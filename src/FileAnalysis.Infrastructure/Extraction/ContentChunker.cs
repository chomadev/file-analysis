using FileAnalysis.Core.Options;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Extraction;

/// <summary>Splits extracted text into overlapping chunks for storage and embedding.</summary>
public class ContentChunker(IOptions<ExtractionOptions> options)
{
    private readonly ExtractionOptions _opts = options.Value;

    /// <summary>Splits <paramref name="text"/> into chunks. Returns a single chunk if text fits.</summary>
    public IReadOnlyList<string> Chunk(string text)
    {
        if (text.Length <= _opts.ChunkSize)
            return [text];

        var chunks = new List<string>();
        var pos = 0;
        while (pos < text.Length)
        {
            var end = Math.Min(pos + _opts.ChunkSize, text.Length);
            // Try to break at whitespace to avoid cutting mid-word
            if (end < text.Length)
            {
                var ws = text.LastIndexOf(' ', end, Math.Min(100, end - pos));
                if (ws > pos) end = ws;
            }
            chunks.Add(text[pos..end].Trim());
            var nextPos = end - _opts.ChunkOverlap;
            // `end` is always > `pos` here, so falling back to `end` guarantees forward progress
            // even when the whitespace search pulled `end` in close enough that overlap would
            // otherwise push `nextPos` backward (or leave it stuck) and loop forever.
            pos = nextPos > pos ? nextPos : end;
        }
        return chunks;
    }
}
