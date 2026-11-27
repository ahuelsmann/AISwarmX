using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Store;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

public sealed class ReviewClaimTests
{
    private const string UpperSha = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
    private const string LowerSha = "abcdef0123456789abcdef0123456789abcdef01";

    [Theory]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("abcdef0123456789abcdef0123456789abcdef012")]
    public void RejectsInvalidHeadSha(string badSha)
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        Assert.Throws<ArgumentException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "claude-opus",
            PrNumber: 56,
            HeadSha: badSha,
            Role: "companion")));
    }

    [Fact]
    public void NormalizesShaEnforcesSingleHolderRecoversOnExpiryAndLocksOnComplete()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        var claim1 = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "claude-opus",
            PrNumber: 56,
            HeadSha: UpperSha,
            Role: "companion",
            TtlMinutes: 30));

        Assert.True(claim1.Ok);
        Assert.True(claim1.Claimed);
        Assert.False(claim1.Reclaimed);
        Assert.Equal(LowerSha, claim1.HeadSha);
        Assert.Equal("claimed", claim1.Status);

        // Concurrent second reviewer blocked while active.
        var blocked = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "antigravity-gemini",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.False(blocked.Claimed);
        Assert.Equal("already_claimed", blocked.Reason);
        Assert.False(blocked.AlreadyClaimedBySelf);
        Assert.Equal("claude-opus", blocked.Holder);

        // Same reviewer re-checking sees already_claimed_by_self = true.
        var selfCheck = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "claude-opus",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.False(selfCheck.Claimed);
        Assert.True(selfCheck.AlreadyClaimedBySelf);

        // Advance past TTL -> second reviewer can reclaim.
        clock.Advance(TimeSpan.FromMinutes(31));
        var reclaimed = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "antigravity-gemini",
            PrNumber: 56,
            HeadSha: UpperSha,
            Role: "companion"));
        Assert.True(reclaimed.Claimed);
        Assert.True(reclaimed.Reclaimed);
        Assert.Equal("claude-opus", reclaimed.PreviousHolder);

        // Release by active holder and reclaim by third agent.
        var released = store.ReviewClaim(new ReviewClaimRequest(
            Action: "release",
            Agent: "antigravity-gemini",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.True(released.Released);
        Assert.Equal("released", released.Status);

        var claimedAfterRelease = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "codex",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.True(claimedAfterRelease.Claimed);
        Assert.True(claimedAfterRelease.Reclaimed);

        // Complete -> permanently locked for that (workspace, pr, head_sha, role).
        var completed = store.ReviewClaim(new ReviewClaimRequest(
            Action: "complete",
            Agent: "codex",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.Equal("completed", completed.Status);
        Assert.Equal("completed", completed.Claim?.Status);

        var afterComplete = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "claude-opus",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.False(afterComplete.Claimed);
        Assert.Equal("already_completed", afterComplete.Reason);

        var releaseCompleted = store.ReviewClaim(new ReviewClaimRequest(
            Action: "release",
            Agent: "codex",
            PrNumber: 56,
            HeadSha: LowerSha,
            Role: "companion"));
        Assert.False(releaseCompleted.Released);
        Assert.Equal("already_completed", releaseCompleted.Reason);

        // List with filter.
        var listed = store.ReviewClaim(new ReviewClaimRequest(
            Action: "list",
            PrNumber: 56,
            HeadSha: UpperSha));
        Assert.Single(listed.Claims!);
    }

    [Fact]
    public void CompleteAndReleaseRejectMissingExpiredOrMismatchedHolderClaims()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        // Missing claim cannot be completed or released.
        Assert.Throws<InvalidOperationException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "complete",
            Agent: "codex",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));
        Assert.Throws<InvalidOperationException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "release",
            Agent: "codex",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));

        // Claim by claude-opus; another agent cannot complete or release it.
        store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "claude-opus",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final",
            TtlMinutes: 15));

        Assert.Throws<InvalidOperationException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "complete",
            Agent: "codex",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));
        Assert.Throws<InvalidOperationException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "release",
            Agent: "codex",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));

        // Empty agent is rejected.
        Assert.Throws<ArgumentException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "complete",
            Agent: "",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));

        // Expired lease cannot be completed or released by previous holder without reclaiming.
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Throws<InvalidOperationException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "complete",
            Agent: "claude-opus",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));
        Assert.Throws<InvalidOperationException>(() => store.ReviewClaim(new ReviewClaimRequest(
            Action: "release",
            Agent: "claude-opus",
            PrNumber: 77,
            HeadSha: LowerSha,
            Role: "final")));
    }
}
