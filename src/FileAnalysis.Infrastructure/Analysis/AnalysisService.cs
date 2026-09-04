using System.Text.Json;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Core.Options;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Analysis;

/// <summary>Runs AI analysis and embedding generation for a single indexed file.</summary>
public class AnalysisService(
    IFileRepository fileRepo,
    IFileContentRepository contentRepo,
    IFileAnalysisRepository analysisRepo,
    IOllamaClient ollama,
    IOptions<OllamaOptions> options)
{
    private const int MaxPromptChars = 8_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly OllamaOptions _options = options.Value;

    /// <summary>
    /// Analyzes <paramref name="fileId"/> and stores the result. Runs even when no content could be extracted —
    /// the filename alone is often a strong signal (e.g. "pikachu.3mf") — falling back gracefully otherwise.
    /// Best-effort: failures are logged, not thrown.
    /// </summary>
    public async Task AnalyzeAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await fileRepo.GetByIdAsync(fileId, ct);
        if (file is null)
            return;

        // Accumulate chunks (ordered by ChunkIndex) only up to MaxPromptChars instead of fetching and
        // joining every chunk for the file first — chunk length varies with ContentChunker's
        // whitespace-boundary trimming, so we can't just Take(N) a fixed chunk count up front.
        var promptBuilder = new System.Text.StringBuilder();
        await foreach (var chunk in contentRepo.StreamChunksAsync(fileId, ct))
        {
            if (promptBuilder.Length > 0)
                promptBuilder.Append('\n');
            promptBuilder.Append(chunk.Content);
            if (promptBuilder.Length >= MaxPromptChars)
                break;
        }

        string? content = promptBuilder.Length > 0 ? promptBuilder.ToString() : null;
        if (content is { Length: > MaxPromptChars })
            content = content[..MaxPromptChars];

        string rawJson;
        try
        {
            rawJson = await ollama.GenerateAsync(_options.AnalysisModel, BuildPrompt(file.Name, file.Extension, content), ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[analysis] {fileId}: analysis request failed — {ex.Message}");
            return;
        }

        var parsed = ParseAnalysis(rawJson);

        float[]? embedding = null;
        try
        {
            var embedSource = !string.IsNullOrWhiteSpace(parsed?.Summary) ? parsed!.Summary!
                : !string.IsNullOrWhiteSpace(content) ? content!
                : file.Name;
            embedding = await ollama.EmbedAsync(_options.EmbeddingModel, embedSource, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[analysis] {fileId}: embedding request failed — {ex.Message}");
        }

        var result = new FileAnalysisResult
        {
            FileId = fileId,
            Summary = parsed?.Summary,
            Category = parsed?.Category,
            Language = parsed?.Language,
            Tags = parsed?.Tags ?? [],
            Topics = parsed?.Topics ?? [],
            UsefulnessScore = parsed?.UsefulnessScore,
            IsDuplicateCandidate = parsed?.IsDuplicateCandidate,
            RawResponse = rawJson,
            Embedding = embedding,
            AnalyzedAt = DateTime.UtcNow,
        };

        await analysisRepo.UpsertAsync(result, ct);
    }

    private static AnalysisJson? ParseAnalysis(string rawJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AnalysisJson>(rawJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private const string ResponseSchema = """
        {
          "summary": "one or two sentence summary of the content",
          "category": "short category, e.g. code, document, config, data, image-description",
          "language": "primary human or programming language, or null if not applicable",
          "tags": ["short", "lowercase", "keywords"],
          "topics": ["broader topics or subject areas covered"],
          "usefulness_score": integer from 1 to 10 rating how valuable this file likely is to keep,
          "is_duplicate_candidate": true or false — true if this looks like a duplicate, backup, or temp file
        }
        """;

    private static string BuildPrompt(string fileName, string? extension, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $$"""
                No text content could be extracted from this file — it is likely a binary or unsupported format
                (e.g. a 3D model, image, or other non-text asset). Base your analysis on the filename alone;
                it is often a strong signal of what the file is (e.g. "pikachu.3mf" is clearly a Pikachu model).

                Filename: {{fileName}}
                Extension: {{extension}}

                Respond with ONLY a single JSON object with exactly these fields:
                {{ResponseSchema}}
                """;
        }

        return $$"""
            Filename: {{fileName}}

            Analyze the following file content and respond with ONLY a single JSON object with exactly these fields:
            {{ResponseSchema}}

            File content:
            ---
            {{content}}
            ---
            """;
    }
}
