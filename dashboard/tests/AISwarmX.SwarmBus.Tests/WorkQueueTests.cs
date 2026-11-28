using AISwarmX.SwarmBus.Core.Contracts;
using AISwarmX.SwarmBus.Core.Store;
using Xunit;

namespace AISwarmX.SwarmBus.Tests;

public sealed class WorkQueueTests
{
    [Fact]
    public void QueueNextResumesOwnLeaseRenews4hAndAllowsExpiredTakeover()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 10, Title: "Low prio", Priority: 2, RiskClass: "M"));
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 20, Title: "High prio", Priority: 0, RiskClass: "M"));

        var first = store.QueueNext("codex");
        Assert.True(first.Ok);
        Assert.False(first.Resumed);
        Assert.NotNull(first.Item);
        Assert.Equal(20, first.Item.IssueNumber);
        Assert.Equal(clock.GetUtcNow().AddHours(4), first.Item.LeaseExpiresUtc);

        // Advance 1h: same agent resumes own active claim and lease renews +4h from now.
        clock.Advance(TimeSpan.FromHours(1));
        var resumed = store.QueueNext("codex");
        Assert.True(resumed.Resumed);
        Assert.Equal(20, resumed.Item!.IssueNumber);
        Assert.Equal(clock.GetUtcNow().AddHours(4), resumed.Item.LeaseExpiresUtc);

        // Advance past 4h lease expiry: another agent takes over expired claim.
        clock.Advance(TimeSpan.FromMinutes(241));
        var takeover = store.QueueNext("antigravity-gemini");
        Assert.False(takeover.Resumed);
        Assert.Equal(20, takeover.Item!.IssueNumber);
        Assert.Equal("antigravity-gemini", takeover.Item.ClaimedBy);
    }

    [Fact]
    public void QueueNextUsesFifoOrderWithinSamePriority()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 301, Title: "First P1", Priority: 1, RiskClass: "L"));
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 302, Title: "Second P1", Priority: 1, RiskClass: "L"));

        var first = store.QueueNext("agent-1");
        var second = store.QueueNext("agent-2");

        Assert.Equal(301, first.Item!.IssueNumber);
        Assert.Equal(302, second.Item!.IssueNumber);
    }

    [Fact]
    public void EffectiveCodeWipGateAllowsOnlyClassLAndCountsUnclassifiedAsCode()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        store.SyncMeta("code_wip", "1");
        // Unclassified active claim counts as +1 code WIP -> effectiveCodeWip = 2.
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 101, Title: "Unclassified", Priority: 1, RiskClass: null));
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 102, Title: "Code H", Priority: 0, RiskClass: "H"));
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 103, Title: "Docs L", Priority: 3, RiskClass: "L"));

        // First claim gets priority 0 ("Code H"), effectiveCodeWip was 1 (< 2).
        var claim1 = store.QueueNext("agent-a");
        Assert.Equal(102, claim1.Item!.IssueNumber);
        Assert.Equal(1, claim1.EffectiveCodeWip);

        // Now effectiveCodeWip = 1 (meta) + 1 (issue 102) = 2 -> gated to L-only!
        var claim2 = store.QueueNext("agent-b");
        Assert.NotNull(claim2.Item);
        Assert.Equal(103, claim2.Item.IssueNumber);
        Assert.Equal(2, claim2.EffectiveCodeWip);

        // Verify unclassified item (101) cannot be claimed while effectiveCodeWip >= 2,
        // and once 101 is claimed (when base wip=0), it counts toward activeClaimedCodeCount.
        store.QueueDone(102);
        store.SyncMeta("code_wip", "0");
        var claimUnclassified = store.QueueNext("agent-c");
        Assert.Equal(101, claimUnclassified.Item!.IssueNumber);

        store.SyncMeta("code_wip", "1");
        // Now meta=1 + unclassified(101)=1 => effectiveCodeWip=2, no L items left -> null!
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 104, Title: "Code M", Priority: 0, RiskClass: "M"));
        var blocked = store.QueueNext("agent-d");
        Assert.Null(blocked.Item);
        Assert.Equal(2, blocked.EffectiveCodeWip);
        Assert.Contains("L-class", blocked.Reason);
    }

    [Fact]
    public void UpsertPreservesClaimedStatusAndWorkspaceIsolationWorks()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 56, Title: "SwarmBus Core", Priority: 1, RiskClass: "H", WorkspaceId: "mobadev"));
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 56, Title: "MOBAflow #56", Priority: 1, RiskClass: "H", WorkspaceId: "mobaflow"));

        var claimedDev = store.QueueNext("antigravity", workspaceId: "mobadev");
        Assert.Equal(56, claimedDev.Item!.IssueNumber);
        Assert.Equal("mobadev", claimedDev.Item.WorkspaceId);

        // Watchdog upsert sync with status='queued' must preserve 'claimed'.
        var synced = store.QueueUpsert(new WorkQueueUpsertRequest(
            IssueNumber: 56,
            Title: "SwarmBus Core Updated",
            Priority: 0,
            RiskClass: "M",
            Status: "queued",
            WorkspaceId: "mobadev"));
        Assert.Equal("claimed", synced.Item!.Status);
        Assert.Equal("antigravity", synced.Item.ClaimedBy);
        Assert.Equal("SwarmBus Core Updated", synced.Item.Title);

        // Separate workspace still has issue #56 queued.
        var flowList = store.QueueList(workspaceId: "mobaflow");
        var flowItem = Assert.Single(flowList.Items);
        Assert.Equal("queued", flowItem.Status);

        // Release & Done lifecycle.
        var released = store.QueueRelease(56, agent: "antigravity", workspaceId: "mobadev");
        Assert.True(released.Changed);
        Assert.Equal("queued", released.Item!.Status);
        Assert.Null(released.Item.ClaimedBy);

        var done = store.QueueDone(56, workspaceId: "mobadev");
        Assert.True(done.Changed);
        Assert.Equal("done", done.Item!.Status);
    }

    [Fact]
    public async Task QueueResumeRenewsActiveLeaseAndReturnsLeaseLostWithoutClaimingNewWork()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 10, Title: "Queued item", Priority: 1, RiskClass: "L"));
        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 20, Title: "Active item", Priority: 0, RiskClass: "M"));

        // Neither agent holds a claim yet: resume must fail with lease_lost and NOT claim #20.
        var noLease = store.QueueResume(20, "codex");
        Assert.False(noLease.Ok);
        Assert.False(noLease.Resumed);
        Assert.Null(noLease.Item);
        Assert.Equal("lease_lost", noLease.Reason);
        Assert.Equal("queued", store.QueueList().Items.Single(i => i.IssueNumber == 20).Status);

        // Claim #20 via QueueNext.
        var claimed = await store.QueueNextAsync("codex");
        Assert.True(claimed.Ok);
        Assert.Equal(20, claimed.Item!.IssueNumber);

        // Wrong issue_number held by same agent fails with lease_lost and does not claim #10.
        var wrongIssue = store.QueueResume(10, "codex");
        Assert.False(wrongIssue.Ok);
        Assert.False(wrongIssue.Resumed);
        Assert.Equal("lease_lost", wrongIssue.Reason);
        Assert.Equal("queued", store.QueueList().Items.Single(i => i.IssueNumber == 10).Status);

        // Foreign agent cannot resume codex's claim and must NOT claim #10.
        var foreign = await store.QueueResumeAsync(20, "claude-opus");
        Assert.False(foreign.Ok);
        Assert.False(foreign.Resumed);
        Assert.Null(foreign.Item);
        Assert.Equal("lease_lost", foreign.Reason);
        Assert.Equal("queued", store.QueueList().Items.Single(i => i.IssueNumber == 10).Status);

        // Same agent renews after 1h -> lease extends +4h from current clock.
        clock.Advance(TimeSpan.FromHours(1));
        var renewed = await store.QueueResumeAsync(20, "CODEX");
        Assert.True(renewed.Ok);
        Assert.True(renewed.Resumed);
        Assert.Equal(20, renewed.Item!.IssueNumber);
        Assert.Equal(clock.GetUtcNow().AddHours(4), renewed.Item.LeaseExpiresUtc);

        // Once lease expires, QueueResume returns lease_lost and never falls through to claim #10 or re-claim #20.
        clock.Advance(TimeSpan.FromMinutes(241));
        var expired = store.QueueResume(20, "codex");
        Assert.False(expired.Ok);
        Assert.False(expired.Resumed);
        Assert.Null(expired.Item);
        Assert.Equal("lease_lost", expired.Reason);
        Assert.Equal("queued", store.QueueList().Items.Single(i => i.IssueNumber == 10).Status);
    }

    [Fact]
    public async Task QueueDoneWithAtomicGuardsEnforcesStatusOwnerAndClaimedBefore()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 50, Title: "Guarded task", Priority: 0, RiskClass: "M"));
        var claimed = store.QueueNext("codex");
        var claimedAt = claimed.Item!.ClaimedAtUtc!.Value;

        // Wrong owner -> guard_mismatch.
        var wrongOwner = store.QueueDone(50, expectedStatus: "claimed", expectedOwner: "claude-opus");
        Assert.False(wrongOwner.Ok);
        Assert.False(wrongOwner.Changed);
        Assert.Equal("guard_mismatch", wrongOwner.Reason);
        Assert.Equal("claimed", wrongOwner.Item!.Status);

        // Wrong status -> guard_mismatch.
        var wrongStatus = await store.QueueDoneAsync(50, expectedStatus: "queued", expectedOwner: "codex");
        Assert.False(wrongStatus.Ok);
        Assert.False(wrongStatus.Changed);
        Assert.Equal("guard_mismatch", wrongStatus.Reason);

        // ClaimedBeforeUtc equal to claimedAt (strictly '<' fails) -> guard_mismatch.
        var tooEarly = await store.QueueDoneAsync(new WorkQueueDoneRequest(
            IssueNumber: 50,
            ExpectedStatus: "claimed",
            ExpectedOwner: "codex",
            ClaimedBeforeUtc: claimedAt));
        Assert.False(tooEarly.Ok);
        Assert.False(tooEarly.Changed);
        Assert.Equal("guard_mismatch", tooEarly.Reason);

        // Matching all guards -> succeeds.
        var succeeded = await store.QueueDoneAsync(new WorkQueueDoneRequest(
            IssueNumber: 50,
            ExpectedStatus: "claimed",
            ExpectedOwner: "CODEX",
            ClaimedBeforeUtc: claimedAt.AddSeconds(1)));
        Assert.True(succeeded.Ok);
        Assert.True(succeeded.Changed);
        Assert.Null(succeeded.Reason);
        Assert.Equal("done", succeeded.Item!.Status);

        // Re-running with expectedStatus='claimed' on now-done row -> guard_mismatch.
        var staleRepeat = store.QueueDone(50, expectedStatus: "claimed");
        Assert.False(staleRepeat.Ok);
        Assert.False(staleRepeat.Changed);
        Assert.Equal("guard_mismatch", staleRepeat.Reason);

        // Non-existent issue returns Ok = true, Changed = false, Item = null.
        var missing = store.QueueDone(999, expectedStatus: "claimed");
        Assert.True(missing.Ok);
        Assert.False(missing.Changed);
        Assert.Null(missing.Item);
    }
}
