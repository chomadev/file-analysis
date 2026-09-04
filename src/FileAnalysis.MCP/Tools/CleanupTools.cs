using System.ComponentModel;
using System.Text.Json;
using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Infrastructure.Cleanup;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FileAnalysis.MCP.Tools;

/// <summary>
/// MCP tools for reviewing cleanup suggestions conversationally. Deliberately does NOT expose an
/// "apply" tool — moving files into quarantine is a CLI-only, human-run action
/// (<see cref="CleanupApplier"/> is never registered in this project's DI container at all).
/// Kept in a separate file from <see cref="FileSearchTools"/> so that safety boundary is visible
/// at a glance, not just enforced by omission.
/// </summary>
[McpServerToolType]
public sealed class CleanupTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description("Scan the index for files that may be safe to remove (exact/near duplicates, low AI-usefulness score, or stale) and record suggestions for human review. Never modifies or deletes any file.")]
    public static async Task<string> SuggestCleanup(
        IServiceScopeFactory scopeFactory,
        [Description("Only consider files under this path prefix. If omitted, the whole index is scanned (capped at a safety limit).")] string? path = null,
        [Description("Override the configured stale-days threshold for this run")] int? olderThanDays = null,
        [Description("Override the configured low-usefulness threshold for this run")] int? minUsefulness = null,
        [Description("Override the configured near-duplicate similarity threshold (0-1) for this run")] double? similarityThreshold = null,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CleanupSuggestionService>();

        var result = await service.GenerateAsync(path, olderThanDays, minUsefulness, similarityThreshold, dryRun: false, ct);
        return JsonSerializer.Serialize(new
        {
            created = result.Created.Select(c => new { c.FileId, c.Path, reason = c.Reason.ToString(), c.Details }),
            skippedAlreadyDecided = result.Skipped,
        }, JsonOptions);
    }

    [McpServerTool, Description("List cleanup suggestions, optionally filtered by status or path prefix.")]
    public static async Task<string> ListCleanupSuggestions(
        IServiceScopeFactory scopeFactory,
        [Description("Filter by status: pending, approved, rejected, or applied")] string? status = null,
        [Description("Filter by path prefix")] string? path = null,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICleanupSuggestionRepository>();

        CleanupStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<CleanupStatus>(status, ignoreCase: true, out var s))
                return JsonSerializer.Serialize(new { error = $"Unknown status '{status}'." }, JsonOptions);
            parsedStatus = s;
        }

        var items = await repo.ListAsync(parsedStatus, path, ct);
        return JsonSerializer.Serialize(items.Select(s => new
        {
            id = s.Id.ToString("N")[..8],
            fullId = s.Id,
            status = s.Status.ToString(),
            reason = s.Reason.ToString(),
            path = s.File?.Path,
            s.Details,
            s.SuggestedAt,
        }), JsonOptions);
    }

    [McpServerTool, Description("Approve a single pending cleanup suggestion by ID (or unambiguous ID prefix). This only marks it approved — actually moving the file requires the apply-cleanup CLI command, run directly by a human.")]
    public static async Task<string> ApproveCleanup(
        IServiceScopeFactory scopeFactory,
        [Description("Suggestion ID or unambiguous prefix, as returned by list_cleanup_suggestions")] string id,
        CancellationToken ct = default) =>
        await SetStatusAsync(scopeFactory, id, CleanupStatus.Approved, ct);

    [McpServerTool, Description("Reject a single pending cleanup suggestion by ID (or unambiguous ID prefix).")]
    public static async Task<string> RejectCleanup(
        IServiceScopeFactory scopeFactory,
        [Description("Suggestion ID or unambiguous prefix, as returned by list_cleanup_suggestions")] string id,
        CancellationToken ct = default) =>
        await SetStatusAsync(scopeFactory, id, CleanupStatus.Rejected, ct);

    private static async Task<string> SetStatusAsync(IServiceScopeFactory scopeFactory, string id, CleanupStatus newStatus, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICleanupSuggestionRepository>();

        CleanupSuggestion? suggestion;
        try
        {
            suggestion = await repo.GetByIdOrPrefixAsync(id, ct);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
        }

        if (suggestion is null)
            return JsonSerializer.Serialize(new { error = "Suggestion not found." }, JsonOptions);
        if (suggestion.Status != CleanupStatus.Pending)
            return JsonSerializer.Serialize(new { error = $"Suggestion is already {suggestion.Status}." }, JsonOptions);

        suggestion.Status = newStatus;
        suggestion.DecidedAt = DateTime.UtcNow;
        await repo.UpsertAsync(suggestion, ct);

        return JsonSerializer.Serialize(new { ok = true, status = newStatus.ToString() }, JsonOptions);
    }
}
