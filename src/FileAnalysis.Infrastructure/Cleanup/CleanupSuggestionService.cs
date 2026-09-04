using FileAnalysis.Core.Interfaces;
using FileAnalysis.Core.Models;
using FileAnalysis.Core.Options;
using Microsoft.Extensions.Options;

namespace FileAnalysis.Infrastructure.Cleanup;

/// <summary>
/// Detects and records cleanup suggestions. Never touches the filesystem — see <see cref="CleanupApplier"/>
/// for the only place that does. Idempotent: a suggestion whose status has moved past Pending (Approved,
/// Rejected, or Applied) is never overwritten by a later generation run, mirroring FileScanner's
/// "alreadyAnalyzed" gate.
/// </summary>
public class CleanupSuggestionService(ICleanupSuggestionRepository repository, IFileRepository fileRepository, IOptions<CleanupOptions> options)
{
    private readonly CleanupOptions _options = options.Value;

    public async Task<CleanupGenerationResult> GenerateAsync(
        string? pathFilter = null,
        int? staleDaysOverride = null,
        int? minUsefulnessOverride = null,
        double? similarityThresholdOverride = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var staleDays = staleDaysOverride ?? _options.StaleDays;
        var minUsefulness = minUsefulnessOverride ?? _options.MinUsefulnessThreshold;
        var similarityThreshold = similarityThresholdOverride ?? _options.NearDuplicateThreshold;

        int skipped = 0;
        var handled = new HashSet<Guid>();
        var createdItems = new List<CleanupCandidatePreview>();

        // Reason priority: ExactDuplicate > NearDuplicate > LowUsefulness > Stale.
        var exactDuplicates = await repository.FindExactDuplicateGroupsAsync(_options.MinDuplicateSizeBytes, ct);
        foreach (var dup in exactDuplicates)
        {
            if (createdItems.Count >= _options.MaxCandidatesPerRun)
                break;
            if (pathFilter is not null && !dup.Path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var keeperPath = (await fileRepository.GetByIdAsync(dup.KeeperFileId, ct))?.Path ?? dup.KeeperFileId.ToString();
            var created = await TryUpsertAsync(
                dup.FileId, dup.Path, CleanupReason.ExactDuplicate, $"Exact duplicate (identical content) of {keeperPath}", dup.KeeperFileId, dryRun, ct);
            if (created is not null) createdItems.Add(created); else skipped++;
            handled.Add(dup.FileId);
            // The keeper is resolved for this run too — it must never also pick up a NearDuplicate/
            // LowUsefulness/Stale suggestion against the very duplicate we just decided to remove.
            handled.Add(dup.KeeperFileId);
        }

        var nearDuplicates = await repository.FindNearDuplicatesAsync(similarityThreshold, limitPerFile: 3, ct);
        foreach (var (fileId, candidateFileId, similarity) in nearDuplicates)
        {
            if (createdItems.Count >= _options.MaxCandidatesPerRun)
                break;
            if (handled.Contains(fileId))
                continue;

            var file = await fileRepository.GetByIdAsync(fileId, ct);
            if (file is null)
                continue;
            if (pathFilter is not null && !file.Path.StartsWith(pathFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidatePath = (await fileRepository.GetByIdAsync(candidateFileId, ct))?.Path ?? candidateFileId.ToString();
            var created = await TryUpsertAsync(
                fileId, file.Path, CleanupReason.NearDuplicate, $"{similarity:P0} similar to {candidatePath}", candidateFileId, dryRun, ct);
            if (created is not null) createdItems.Add(created); else skipped++;
            handled.Add(fileId);
        }

        var candidateFiles = await repository.ListCandidateFilesAsync(pathFilter, ct);
        foreach (var file in candidateFiles)
        {
            if (createdItems.Count >= _options.MaxCandidatesPerRun)
                break;
            if (handled.Contains(file.FileId))
                continue;

            if (file.UsefulnessScore is { } score && score <= minUsefulness)
            {
                var created = await TryUpsertAsync(file.FileId, file.Path, CleanupReason.LowUsefulness,
                    $"AI usefulness score {score}/10 (threshold {minUsefulness})", null, dryRun, ct);
                if (created is not null) createdItems.Add(created); else skipped++;
                handled.Add(file.FileId);
                continue;
            }

            if (file.FileModifiedAt is { } modified && modified < DateTime.UtcNow.AddDays(-staleDays))
            {
                var created = await TryUpsertAsync(file.FileId, file.Path, CleanupReason.Stale,
                    $"Not modified since {modified:yyyy-MM-dd} ({staleDays}+ days)", null, dryRun, ct);
                if (created is not null) createdItems.Add(created); else skipped++;
                handled.Add(file.FileId);
            }
        }

        return new CleanupGenerationResult(createdItems, skipped);
    }

    /// <summary>Returns the preview of what would be (or was) created, or null if the file already has a
    /// non-Pending (human-decided) suggestion that must not be clobbered.</summary>
    private async Task<CleanupCandidatePreview?> TryUpsertAsync(
        Guid fileId, string path, CleanupReason reason, string details, Guid? duplicateOfFileId, bool dryRun, CancellationToken ct)
    {
        var existing = await repository.GetByFileIdAsync(fileId, ct);
        if (existing is not null && existing.Status != CleanupStatus.Pending)
            return null;

        if (!dryRun)
        {
            await repository.UpsertAsync(new CleanupSuggestion
            {
                FileId = fileId,
                Reason = reason,
                Details = details,
                DuplicateOfFileId = duplicateOfFileId,
                Status = CleanupStatus.Pending,
                SuggestedAt = DateTime.UtcNow,
            }, ct);
        }

        return new CleanupCandidatePreview(fileId, path, reason, details);
    }
}

public record CleanupGenerationResult(IReadOnlyList<CleanupCandidatePreview> Created, int Skipped);

public record CleanupCandidatePreview(Guid FileId, string Path, CleanupReason Reason, string Details);
