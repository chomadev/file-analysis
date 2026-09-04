using FileAnalysis.Infrastructure.Cleanup;
using FluentAssertions;

namespace FileAnalysis.Infrastructure.Tests;

public class DuplicateGroupSelectorTests
{
    [Fact]
    public void SelectKeeper_PrefersHighestUsefulnessScore()
    {
        var a = new DuplicateCandidate(Guid.NewGuid(), "a.txt", UsefulnessScore: 3, IndexedAt: DateTime.UtcNow);
        var b = new DuplicateCandidate(Guid.NewGuid(), "b.txt", UsefulnessScore: 8, IndexedAt: DateTime.UtcNow);

        var keeper = DuplicateGroupSelector.SelectKeeper([a, b], new HashSet<Guid>());

        keeper.Should().Be(b.Id);
    }

    [Fact]
    public void SelectKeeper_TiebreaksOnEarliestIndexedAt()
    {
        var now = DateTime.UtcNow;
        var older = new DuplicateCandidate(Guid.NewGuid(), "old.txt", UsefulnessScore: 5, IndexedAt: now.AddDays(-10));
        var newer = new DuplicateCandidate(Guid.NewGuid(), "new.txt", UsefulnessScore: 5, IndexedAt: now);

        var keeper = DuplicateGroupSelector.SelectKeeper([newer, older], new HashSet<Guid>());

        keeper.Should().Be(older.Id);
    }

    [Fact]
    public void SelectKeeper_NullScoreSortsLast()
    {
        var scored = new DuplicateCandidate(Guid.NewGuid(), "scored.txt", UsefulnessScore: 1, IndexedAt: DateTime.UtcNow);
        var unscored = new DuplicateCandidate(Guid.NewGuid(), "unscored.txt", UsefulnessScore: null, IndexedAt: DateTime.UtcNow);

        var keeper = DuplicateGroupSelector.SelectKeeper([unscored, scored], new HashSet<Guid>());

        keeper.Should().Be(scored.Id);
    }

    [Fact]
    public void SelectKeeper_SkipsCandidatesAlreadyApprovedOrAppliedForRemoval()
    {
        var bestButIneligible = new DuplicateCandidate(Guid.NewGuid(), "a.txt", UsefulnessScore: 9, IndexedAt: DateTime.UtcNow);
        var fallback = new DuplicateCandidate(Guid.NewGuid(), "b.txt", UsefulnessScore: 2, IndexedAt: DateTime.UtcNow);

        var keeper = DuplicateGroupSelector.SelectKeeper(
            [bestButIneligible, fallback], ineligibleKeepers: new HashSet<Guid> { bestButIneligible.Id });

        keeper.Should().Be(fallback.Id);
    }

    [Fact]
    public void SelectKeeper_AllIneligible_FallsBackToNormalOrdering()
    {
        var best = new DuplicateCandidate(Guid.NewGuid(), "a.txt", UsefulnessScore: 9, IndexedAt: DateTime.UtcNow);
        var worse = new DuplicateCandidate(Guid.NewGuid(), "b.txt", UsefulnessScore: 1, IndexedAt: DateTime.UtcNow);

        var keeper = DuplicateGroupSelector.SelectKeeper(
            [worse, best], ineligibleKeepers: new HashSet<Guid> { best.Id, worse.Id });

        keeper.Should().Be(best.Id);
    }
}
