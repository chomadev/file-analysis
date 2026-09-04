namespace FileAnalysis.Core.Models;

/// <summary>A suggestion that a file may be safe to remove, pending human approval. Never acted on
/// automatically — see <see cref="CleanupStatus.Approved"/> vs <see cref="CleanupStatus.Applied"/>.</summary>
public class CleanupSuggestion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public CleanupStatus Status { get; set; } = CleanupStatus.Pending;
    public CleanupReason Reason { get; set; }
    public string? Details { get; set; }

    /// <summary>For duplicate reasons: the file suggested to keep instead of this one.</summary>
    public Guid? DuplicateOfFileId { get; set; }

    public DateTime SuggestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? QuarantinePath { get; set; }

    public IndexedFile? File { get; set; }
    public IndexedFile? DuplicateOf { get; set; }
}

public enum CleanupStatus { Pending, Approved, Rejected, Applied }

public enum CleanupReason { ExactDuplicate, NearDuplicate, LowUsefulness, Stale }
