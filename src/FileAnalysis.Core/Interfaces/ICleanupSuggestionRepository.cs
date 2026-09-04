using FileAnalysis.Core.Models;

namespace FileAnalysis.Core.Interfaces;

/// <summary>Duplicate/staleness detection queries plus persistence for cleanup suggestions.</summary>
public interface ICleanupSuggestionRepository
{
    /// <summary>Groups files sharing an identical hash. Each tuple is one non-keeper member of a group.</summary>
    Task<IReadOnlyList<(Guid FileId, string Path, long SizeBytes, Guid KeeperFileId)>> FindExactDuplicateGroupsAsync(
        long minSizeBytes, CancellationToken ct = default);

    /// <summary>For each file with an embedding, its closest same-extension neighbor(s) above the caller's threshold filter.</summary>
    Task<IReadOnlyList<(Guid FileId, Guid CandidateFileId, double Similarity)>> FindNearDuplicatesAsync(
        double threshold, int limitPerFile, CancellationToken ct = default);

    /// <summary>All files (optionally scoped by path prefix) with the scalar signals the service needs to
    /// evaluate LowUsefulness/Stale policy — kept as raw data here, decided by the service.</summary>
    Task<IReadOnlyList<(Guid FileId, string Path, int? UsefulnessScore, DateTime? FileModifiedAt)>> ListCandidateFilesAsync(
        string? pathPrefix, CancellationToken ct = default);

    Task<CleanupSuggestion?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default);
    Task<CleanupSuggestion?> GetByIdOrPrefixAsync(string idOrPrefix, CancellationToken ct = default);
    Task UpsertAsync(CleanupSuggestion suggestion, CancellationToken ct = default);
    Task<IReadOnlyList<CleanupSuggestion>> ListAsync(CleanupStatus? status, string? pathPrefix, CancellationToken ct = default);
    Task<int> BulkSetStatusAsync(CleanupStatus from, CleanupStatus to, CleanupReason? reasonFilter, string? pathPrefix, CancellationToken ct = default);
}
