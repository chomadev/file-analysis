using FileAnalysis.Core;
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
public class CleanupSuggestionService(ICleanupSuggestionRepository repository, IOptions<CleanupOptions> options)
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

        var staleCutoff = DateTime.UtcNow.AddDays(-staleDays);

        int skipped = 0;
        var handled = new HashSet<Guid>();
        // Defensive guard only — `handled` already guarantees TryUpsertAsync runs at most once per FileId
        // per run (see the comment on TryUpsertAsync), which is what makes the up-front batch-read of
        // existing-suggestion status below safe to use for the whole run without going stale. This set
        // just makes that invariant fail loudly instead of silently double-writing if it's ever broken.
        var writtenThisRun = new HashSet<Guid>();
        var createdItems = new List<CleanupCandidatePreview>();

        // Reason priority: ExactDuplicate > NearDuplicate > LowUsefulness > Stale.
        var exactDuplicates = await repository.FindExactDuplicateGroupsAsync(_options.MinDuplicateSizeBytes, ct);
        var nearDuplicates = await repository.FindNearDuplicatesAsync(similarityThreshold, limitPerFile: 3, ct);
        var candidateFiles = await repository.ListCandidateFilesAsync(pathFilter, minUsefulness, staleCutoff, ct);

        // Batch-resolve everything the loops below would otherwise fetch one file at a time: display
        // paths for duplicate keepers/candidates (candidateFiles and the exact-duplicate members already
        // carry their own Path), and the existing-suggestion status for every FileId any loop might touch.
        var idsNeedingPath = new HashSet<Guid>();
        foreach (var dup in exactDuplicates)
            idsNeedingPath.Add(dup.KeeperFileId);
        foreach (var nearDup in nearDuplicates)
        {
            idsNeedingPath.Add(nearDup.FileId);
            idsNeedingPath.Add(nearDup.CandidateFileId);
        }
        var pathsById = await repository.GetPathsByIdsAsync(idsNeedingPath, ct);

        var allCandidateFileIds = exactDuplicates.Select(d => d.FileId)
            .Concat(nearDuplicates.Select(n => n.FileId))
            .Concat(candidateFiles.Select(f => f.FileId))
            .ToHashSet();
        var existingStatusByFileId = await repository.GetStatusesByFileIdsAsync(allCandidateFileIds, ct);

        foreach (var dup in exactDuplicates)
        {
            if (createdItems.Count >= _options.MaxCandidatesPerRun)
                break;
            if (pathFilter is not null && !dup.Path.StartsWith(pathFilter, PathComparison.Comparison))
                continue;

            var keeperPath = pathsById.GetValueOrDefault(dup.KeeperFileId, dup.KeeperFileId.ToString());
            var created = await TryUpsertAsync(
                dup.FileId, dup.Path, CleanupReason.ExactDuplicate, $"Exact duplicate (identical content) of {keeperPath}",
                dup.KeeperFileId, existingStatusByFileId, writtenThisRun, dryRun, ct);
            if (created is not null) createdItems.Add(created); else skipped++;
            handled.Add(dup.FileId);
            // The keeper is resolved for this run too — it must never also pick up a NearDuplicate/
            // LowUsefulness/Stale suggestion against the very duplicate we just decided to remove.
            handled.Add(dup.KeeperFileId);
        }

        foreach (var (fileId, candidateFileId, similarity) in nearDuplicates)
        {
            if (createdItems.Count >= _options.MaxCandidatesPerRun)
                break;
            if (handled.Contains(fileId))
                continue;

            if (!pathsById.TryGetValue(fileId, out var filePath))
                continue; // file no longer exists
            if (pathFilter is not null && !filePath.StartsWith(pathFilter, PathComparison.Comparison))
                continue;

            var candidatePath = pathsById.GetValueOrDefault(candidateFileId, candidateFileId.ToString());
            var created = await TryUpsertAsync(
                fileId, filePath, CleanupReason.NearDuplicate, $"{similarity:P0} similar to {candidatePath}",
                candidateFileId, existingStatusByFileId, writtenThisRun, dryRun, ct);
            if (created is not null) createdItems.Add(created); else skipped++;
            handled.Add(fileId);
        }

        foreach (var file in candidateFiles)
        {
            if (createdItems.Count >= _options.MaxCandidatesPerRun)
                break;
            if (handled.Contains(file.FileId))
                continue;

            // ListCandidateFilesAsync already guarantees every row here satisfies
            // (UsefulnessScore <= minUsefulness) OR (FileModifiedAt < staleCutoff); these checks now only
            // decide which reason applies, not whether the file qualifies at all.
            if (file.UsefulnessScore is { } score && score <= minUsefulness)
            {
                var created = await TryUpsertAsync(file.FileId, file.Path, CleanupReason.LowUsefulness,
                    $"AI usefulness score {score}/10 (threshold {minUsefulness})", null, existingStatusByFileId, writtenThisRun, dryRun, ct);
                if (created is not null) createdItems.Add(created); else skipped++;
                handled.Add(file.FileId);
                continue;
            }

            if (file.FileModifiedAt is { } modified && modified < staleCutoff)
            {
                var created = await TryUpsertAsync(file.FileId, file.Path, CleanupReason.Stale,
                    $"Not modified since {modified:yyyy-MM-dd} ({staleDays}+ days)", null, existingStatusByFileId, writtenThisRun, dryRun, ct);
                if (created is not null) createdItems.Add(created); else skipped++;
                handled.Add(file.FileId);
            }
        }

        return new CleanupGenerationResult(createdItems, skipped);
    }

    /// <summary>Returns the preview of what would be (or was) created, or null if the file already has a
    /// non-Pending (human-decided) suggestion that must not be clobbered. <paramref name="existingStatusByFileId"/>
    /// is a snapshot taken once at the start of the run — safe because the caller's `handled` set already
    /// guarantees this is invoked at most once per FileId per run, so no write here can make another
    /// candidate's snapshot entry stale mid-run.</summary>
    private async Task<CleanupCandidatePreview?> TryUpsertAsync(
        Guid fileId, string path, CleanupReason reason, string details, Guid? duplicateOfFileId,
        IReadOnlyDictionary<Guid, CleanupStatus> existingStatusByFileId, HashSet<Guid> writtenThisRun, bool dryRun, CancellationToken ct)
    {
        if (existingStatusByFileId.TryGetValue(fileId, out var existingStatus) && existingStatus != CleanupStatus.Pending)
            return null;

        if (!dryRun)
        {
            if (!writtenThisRun.Add(fileId))
                throw new InvalidOperationException(
                    $"Cleanup suggestion for file {fileId} was about to be written twice in the same run — " +
                    "this should be impossible given the `handled` gating in GenerateAsync.");

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
