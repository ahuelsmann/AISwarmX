using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Locking;
using MobaDev.SwarmBus.Core.Store;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

public sealed class LockFencingTests
{
    [Theory]
    [InlineData("SharedUI", "SharedUI/Desktop", true)]
    [InlineData("SharedUI/Desktop", "SharedUI", true)]
    [InlineData("./SharedUI/", "sharedui\\desktop/*", true)]
    [InlineData("SharedUI/*.cs", "SharedUI/App.cs", true)]
    [InlineData("SharedUI/*.cs", "SharedUI/App.txt", false)]
    [InlineData("SharedUI/*.cs", "SharedUI/Desktop/App.cs", false)]
    [InlineData("SharedUI/**/*.cs", "SharedUI/Desktop/App.cs", true)]
    [InlineData("SharedUI/**/*.cs", "SharedUI/Desktop", true)]
    [InlineData("SharedUI/Desktop", "SharedUI/Web", false)]
    public void PathPatternMatcherDetectsHierarchicalAndGlobOverlaps(string pathA, string pathB, bool expectedOverlap)
    {
        Assert.Equal(expectedOverlap, PathPatternMatcher.PathsOverlap(pathA, pathB));
    }

    [Fact]
    public void ClaimDetectsHierarchicalOverlapAndExplicitReleasePreservesSiblingPrLocks()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var claim1 = store.ClaimLocks(new LockClaimRequest(
            Agent: "claude-opus",
            Paths: ["SharedUI"],
            PrNumber: 537));
        Assert.True(claim1.Ok);

        // Overlapping child directory blocked for another agent.
        var conflict = store.ClaimLocks(new LockClaimRequest(
            Agent: "antigravity-gemini",
            Paths: ["SharedUI/Desktop"],
            PrNumber: 539));
        Assert.False(conflict.Ok);
        Assert.Single(conflict.Conflicts);
        Assert.Equal("claude-opus", conflict.Conflicts[0].ConflictingAgent);
        Assert.Equal("SharedUI", conflict.Conflicts[0].ConflictingPath);

        // Same agent claims another lock on a sibling PR.
        var claimSiblingPr = store.ClaimLocks(new LockClaimRequest(
            Agent: "claude-opus",
            Paths: ["docs/wiki"],
            PrNumber: 540));
        Assert.True(claimSiblingPr.Ok);
        Assert.Equal(2, claimSiblingPr.Locks.Count);

        // Explicit path release removes only SharedUI, preserving PR #540 lock.
        var release = store.ReleaseLocks(new LockReleaseRequest(
            Agent: "claude-opus",
            Paths: ["./SharedUI/"]));
        Assert.True(release.Ok);
        Assert.Equal(1, release.RemovedCount);
        var remaining = Assert.Single(release.RemainingLocks);
        Assert.Equal("docs/wiki", remaining.PathPattern);
        Assert.Equal(540, remaining.PrNumber);

        // Now antigravity-gemini can claim SharedUI/Desktop.
        var claim2 = store.ClaimLocks(new LockClaimRequest(
            Agent: "antigravity-gemini",
            Paths: ["SharedUI/Desktop"],
            PrNumber: 539));
        Assert.True(claim2.Ok);
    }

    [Fact]
    public void ExpiredLocksAreAutoPrunedAndNoLongerBlockClaims()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        var claim1 = store.ClaimLocks(new LockClaimRequest(
            Agent: "codex",
            Paths: ["src/Core"],
            PrNumber: 100,
            TtlMinutes: 10));
        Assert.True(claim1.Ok);

        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Empty(store.ListLocks());

        var claim2 = store.ClaimLocks(new LockClaimRequest(
            Agent: "antigravity-gemini",
            Paths: ["src/Core/Store.cs"],
            PrNumber: 101));
        Assert.True(claim2.Ok);
    }

    [Fact]
    public void MultiWorkspaceIsolationPreventsCrossProjectLockCollisions()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var mobaflowClaim = store.ClaimLocks(new LockClaimRequest(
            Agent: "codex",
            Paths: ["AGENTS.md", "src/Shared"],
            PrNumber: 501,
            WorkspaceId: "mobaflow"));
        Assert.True(mobaflowClaim.Ok);

        // Different agent claiming the exact same paths in 'mobadev' must succeed.
        var mobadevClaim = store.ClaimLocks(new LockClaimRequest(
            Agent: "antigravity-gemini",
            Paths: ["AGENTS.md", "src/Shared/Item.cs"],
            PrNumber: 56,
            WorkspaceId: "mobadev"));
        Assert.True(mobadevClaim.Ok);

        Assert.Equal(2, store.ListLocks("mobaflow").Count);
        Assert.Equal(2, store.ListLocks("mobadev").Count);
        Assert.Equal(4, store.ListLocks("*").Count);

        // Releasing mobadev lock does not touch mobaflow locks.
        store.ReleaseLocks(new LockReleaseRequest(Agent: "antigravity-gemini", WorkspaceId: "mobadev"));
        Assert.Empty(store.ListLocks("mobadev"));
        Assert.Equal(2, store.ListLocks("mobaflow").Count);

        // PR-scoped release removes only locks for that PR, preserving sibling PR locks by the same agent.
        store.ClaimLocks(new LockClaimRequest(Agent: "codex", Paths: ["docs/readme.md"], PrNumber: 502, WorkspaceId: "mobaflow"));
        var prRelease = store.ReleaseLocks(new LockReleaseRequest(Agent: "codex", PrNumber: 501, WorkspaceId: "mobaflow"));
        Assert.Equal(2, prRelease.RemovedCount);
        var remainingMobaflow = Assert.Single(prRelease.RemainingLocks);
        Assert.Equal("docs/readme.md", remainingMobaflow.PathPattern);
        Assert.Equal(502, remainingMobaflow.PrNumber);
    }
}
