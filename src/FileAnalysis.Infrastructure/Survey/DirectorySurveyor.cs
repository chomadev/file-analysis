using System.Text.Json;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Core.Options;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Survey;

/// <summary>Orchestrates a directory survey: walks the tree, classifies large/ambiguous directories
/// via AI, and persists decisions — without ever re-deciding a directory a prior run already settled.</summary>
public class DirectorySurveyor(
    IDirectorySurveyRepository repository,
    IOllamaClient ollama,
    IOptions<ScannerOptions> scannerOptions,
    IOptions<DirectorySurveyOptions> surveyOptions,
    IOptions<OllamaOptions> ollamaOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ScannerOptions _scannerOptions = scannerOptions.Value;
    private readonly DirectorySurveyOptions _options = surveyOptions.Value;
    private readonly OllamaOptions _ollamaOptions = ollamaOptions.Value;

    public async Task<SurveyResult> SurveyAsync(string rootPath, CancellationToken ct = default)
    {
        var walked = DirectorySurveyWalker.Walk(rootPath, _scannerOptions.ExcludePatterns, _options.MaxDepth);

        // Bulk-preload every previously-surveyed entry under this root in one round trip instead of a
        // per-directory GetByPathAsync lookup. Ordinal comparer matches the DB's exact-match semantics.
        var existingByPath = (await repository.ListByRootAsync(rootPath, ct))
            .ToDictionary(e => e.Path, StringComparer.Ordinal);

        var autoExcluded = new List<DirectorySurveyEntry>();
        var needsConfirmation = new List<DirectorySurveyEntry>();

        foreach (var entry in walked)
        {
            ct.ThrowIfCancellationRequested();

            existingByPath.TryGetValue(entry.Path, out var existing);
            if (existing?.ExcludeDecision is not null)
                continue; // already decided in a previous run — never re-decide or re-prompt

            if (entry.Depth == 0)
            {
                // The scan root itself is never a candidate for exclusion — the user explicitly pointed here.
                entry.ExcludeDecision = false;
                await repository.UpsertAsync(entry, ct);
                continue;
            }

            if (entry.ExcludeDecision == true)
            {
                await repository.UpsertAsync(entry, ct);
                autoExcluded.Add(entry);
                continue;
            }

            var isLarge = entry.TotalFileCount >= _options.LargeFileCountThreshold
                || entry.TotalSizeBytes >= _options.LargeSizeBytesThreshold;

            if (!isLarge)
            {
                entry.ExcludeDecision = false;
                await repository.UpsertAsync(entry, ct);
                continue;
            }

            try
            {
                var (classification, confidence, reason) = await ClassifyWithAiAsync(entry, ct);
                entry.Classification = classification;
                entry.ClassificationSource = ClassificationSource.Ai;
                entry.ClassificationReason = reason;

                if (classification == DirectoryClassification.LikelyJunk && confidence == "high")
                {
                    entry.ExcludeDecision = true;
                    await repository.UpsertAsync(entry, ct);
                    autoExcluded.Add(entry);
                }
                else
                {
                    entry.ExcludeDecision = null;
                    await repository.UpsertAsync(entry, ct);
                    needsConfirmation.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[survey] {entry.Path}: AI classification failed — {ex.Message}");
                entry.Classification = DirectoryClassification.Ambiguous;
                entry.ClassificationSource = ClassificationSource.None;
                entry.ClassificationReason = "AI classification unavailable";
                entry.ExcludeDecision = null;
                await repository.UpsertAsync(entry, ct);
                needsConfirmation.Add(entry);
            }
        }

        return new SurveyResult(autoExcluded, needsConfirmation);
    }

    /// <summary>Records a decision for a previously-undecided entry. <paramref name="source"/> defaults to
    /// Human (an interactive answer); pass Ai when auto-confirming the AI's own suggested classification.</summary>
    public async Task ConfirmAsync(DirectorySurveyEntry entry, bool exclude, ClassificationSource source = ClassificationSource.Human, CancellationToken ct = default)
    {
        entry.ExcludeDecision = exclude;
        entry.ClassificationSource = source;
        await repository.UpsertAsync(entry, ct);
    }

    private async Task<(DirectoryClassification Classification, string Confidence, string Reason)> ClassifyWithAiAsync(
        DirectorySurveyEntry entry, CancellationToken ct)
    {
        var raw = await ollama.GenerateAsync(_ollamaOptions.AnalysisModel, BuildPrompt(entry), ct);
        var parsed = JsonSerializer.Deserialize<AiClassification>(raw, JsonOptions);

        var classification = parsed?.Classification switch
        {
            "likely_junk" => DirectoryClassification.LikelyJunk,
            "likely_content" => DirectoryClassification.LikelyContent,
            _ => DirectoryClassification.Ambiguous,
        };
        return (classification, parsed?.Confidence ?? "low", parsed?.Reason ?? "no reason given");
    }

    private static string BuildPrompt(DirectorySurveyEntry entry) => $$"""
        Analyze this directory's structure and decide whether it looks like a build artifact, dependency
        cache, or other machine-generated directory that is safe to skip indexing — or whether it looks
        like real user content worth keeping. You only have structural metadata, not file contents.

        Directory name: {{Path.GetFileName(entry.Path)}}
        Total files (recursive): {{entry.TotalFileCount}}
        Total size (bytes): {{entry.TotalSizeBytes}}
        Most common file extensions: {{string.Join(", ", entry.TopExtensions)}}

        Respond with ONLY a single JSON object with exactly these fields:
        {
          "classification": "likely_junk" | "likely_content" | "ambiguous",
          "confidence": "high" | "medium" | "low",
          "reason": "one concise sentence"
        }
        """;

    private sealed class AiClassification
    {
        public string? Classification { get; set; }
        public string? Confidence { get; set; }
        public string? Reason { get; set; }
    }
}

public record SurveyResult(IReadOnlyList<DirectorySurveyEntry> AutoExcluded, IReadOnlyList<DirectorySurveyEntry> NeedsConfirmation);
