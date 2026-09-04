namespace FileAnalysis.Infrastructure.Cleanup;

/// <summary>Pure keeper-selection logic for a group of files sharing identical content — extracted from
/// CleanupSuggestionRepository purely for testability (no DB/IO dependency).</summary>
public static class DuplicateGroupSelector
{
    /// <summary>Picks the keeper: highest AI usefulness score first, then earliest indexed, then path —
    /// skipping any candidate whose own suggestion is already Approved/Applied, unless every candidate is.</summary>
    public static Guid SelectKeeper(IReadOnlyList<DuplicateCandidate> candidates, IReadOnlySet<Guid> ineligibleKeepers)
    {
        var ordered = candidates
            .OrderByDescending(c => c.UsefulnessScore ?? -1)
            .ThenBy(c => c.IndexedAt)
            .ThenBy(c => c.Path, StringComparer.Ordinal)
            .ToList();

        return ordered.FirstOrDefault(c => !ineligibleKeepers.Contains(c.Id))?.Id ?? ordered[0].Id;
    }
}

public record DuplicateCandidate(Guid Id, string Path, int? UsefulnessScore, DateTime IndexedAt);
