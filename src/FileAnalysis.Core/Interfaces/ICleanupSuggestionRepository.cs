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

    /// <summary>Files (optionally scoped by path prefix) whose usefulness score is at or below
    /// <paramref name="minUsefulness"/> or whose last-modified time is before <paramref name="staleCutoff"/> —
    /// i.e. everything that could trigger a LowUsefulness or Stale suggestion. The threshold filter is
    /// applied server-side; the service still decides which of the two reasons applies per file.</summary>
    Task<IReadOnlyList<(Guid FileId, string Path, int? UsefulnessScore, DateTime? FileModifiedAt)>> ListCandidateFilesAsync(
        string? pathPrefix, int minUsefulness, DateTime staleCutoff, CancellationToken ct = default);

    Task<CleanupSuggestion?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default);
    Task<CleanupSuggestion?> GetByIdOrPrefixAsync(string idOrPrefix, CancellationToken ct = default);
    Task UpsertAsync(CleanupSuggestion suggestion, CancellationToken ct = default);
    Task<IReadOnlyList<CleanupSuggestion>> ListAsync(CleanupStatus? status, string? pathPrefix, CancellationToken ct = default);
    Task<int> BulkSetStatusAsync(CleanupStatus from, CleanupStatus to, CleanupReason? reasonFilter, string? pathPrefix, CancellationToken ct = default);

    /// <summary>Batch-resolves display paths for a set of file IDs (e.g. duplicate keepers/candidates)
    /// in one query instead of one GetByIdAsync round trip per ID. Missing IDs are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPathsByIdsAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default);

    /// <summary>Batch-resolves the current status of any existing suggestion for a set of file IDs, in one
    /// query instead of one GetByFileIdAsync round trip per ID. A FileId absent from the result has no
    /// existing suggestion.</summary>
    Task<IReadOnlyDictionary<Guid, CleanupStatus>> GetStatusesByFileIdsAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default);
}
