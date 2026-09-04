using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Core.Options;
using FileAnalysis.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Cleanup;

/// <summary>
/// The only type in this codebase that moves a file for cleanup purposes. Deliberately not registered
/// anywhere reachable from the MCP server — applying is a human-only, CLI-only action. Never deletes:
/// approved files are moved into a quarantine directory, never removed outright.
/// </summary>
public class CleanupApplier(FileAnalysisDbContext db, IFileRepository fileRepository, IOptions<CleanupOptions> options)
{
    private readonly CleanupOptions _options = options.Value;

    public async Task<ApplyResult> ApplyAsync(Guid suggestionId, bool dryRun, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock the row so a concurrent apply-cleanup run can't race the same suggestion.
        await db.Database.ExecuteSqlAsync($"SELECT \"Id\" FROM cleanup_suggestions WHERE \"Id\" = {suggestionId} FOR UPDATE", ct);

        var suggestion = await db.CleanupSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId, ct);
        if (suggestion is null)
            return ApplyResult.Failed("Suggestion not found.");
        if (suggestion.Status != CleanupStatus.Approved)
            return ApplyResult.Failed($"Suggestion is '{suggestion.Status}', not Approved — it may have changed since you listed it.");

        var file = await fileRepository.GetByIdAsync(suggestion.FileId, ct);
        if (file is null)
            return ApplyResult.Failed("File record not found.");

        if (suggestion.DuplicateOfFileId is { } keeperId)
        {
            var keeper = await fileRepository.GetByIdAsync(keeperId, ct);
            var keeperGone = keeper is null || !File.Exists(keeper.Path);
            var keeperSuggestion = await db.CleanupSuggestions.AsNoTracking().FirstOrDefaultAsync(s => s.FileId == keeperId, ct);
            var keeperAlreadyApplied = keeperSuggestion?.Status == CleanupStatus.Applied;

            if (keeperGone || keeperAlreadyApplied)
            {
                return ApplyResult.Failed(
                    $"Refusing to quarantine — its keeper ({keeper?.Path ?? keeperId.ToString()}) is missing or already applied. " +
                    "Applying this would leave zero surviving copies.");
            }
        }

        if (!File.Exists(file.Path))
            return ApplyResult.Failed("Source file no longer exists on disk.");

        var destination = BuildQuarantinePath(file.Path);

        if (dryRun)
            return ApplyResult.Success(file.Path, destination, dryRun: true);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        destination = ResolveCollision(destination);

        try
        {
            File.Move(file.Path, destination);
        }
        catch (Exception ex)
        {
            return ApplyResult.Failed($"Move failed: {ex.Message}");
        }

        if (!File.Exists(destination))
            return ApplyResult.Failed("Move reported success but the destination file is missing — not recording state.");

        suggestion.Status = CleanupStatus.Applied;
        suggestion.AppliedAt = DateTime.UtcNow;
        suggestion.QuarantinePath = destination;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ApplyResult.Success(file.Path, destination, dryRun: false);
    }

    private string BuildQuarantinePath(string sourcePath)
    {
        // Strip a Windows drive letter's colon (":" is illegal mid-path) and fold it into a top-level
        // segment instead, e.g. C:\foo\bar.txt -> <QuarantineRoot>\C\foo\bar.txt.
        var relative = sourcePath.Length >= 3 && sourcePath[1] == ':'
            ? Path.Combine(sourcePath[0].ToString(), sourcePath[3..])
            : sourcePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Path.Combine(_options.QuarantineRoot, relative);
    }

    private static string ResolveCollision(string destination)
    {
        if (!File.Exists(destination))
            return destination;

        var dir = Path.GetDirectoryName(destination)!;
        var name = Path.GetFileNameWithoutExtension(destination);
        var ext = Path.GetExtension(destination);

        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}

public record ApplyResult(bool Ok, string? SourcePath, string? DestinationPath, string? Error, bool DryRun)
{
    public static ApplyResult Success(string source, string destination, bool dryRun) => new(true, source, destination, null, dryRun);
    public static ApplyResult Failed(string error) => new(false, null, null, error, false);
}
