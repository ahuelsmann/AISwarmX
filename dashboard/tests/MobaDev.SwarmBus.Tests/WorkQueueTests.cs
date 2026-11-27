using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Store;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

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
}
