using System.Diagnostics;
using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Store;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

public sealed class StartWorkAndWaitTests
{
    [Fact]
    public async Task StartWorkSingleRoundtripClaimsPathsReturnsUnreadMessagesAndForeignLocks()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var store = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        // Foreign active lock + expired lock + own existing lock.
        store.ClaimLocks(new LockClaimRequest(Agent: "claude-opus", Paths: ["docs/wiki"], PrNumber: 12, TtlMinutes: 60));
        store.ClaimLocks(new LockClaimRequest(Agent: "antigravity-gemini", Paths: ["scripts/old"], PrNumber: 13, TtlMinutes: 5));
        store.ClaimLocks(new LockClaimRequest(Agent: "codex", Paths: ["src/Existing"], PrNumber: 56, TtlMinutes: 60));

        clock.Advance(TimeSpan.FromMinutes(10)); // Prunes antigravity-gemini's 5m lock.

        // Messages: one for codex, one broadcast to all, one acked for codex, one for claude-opus.
        var msgCodex = store.SendMessage(new SwarmSendRequest(From: "claude-opus", To: "codex", Topic: "review", Body: "Please check"));
        var msgAll = store.SendMessage(new SwarmSendRequest(From: "watchdog", To: "all", Topic: "notice", Body: "Broadcast"));
        var msgAcked = store.SendMessage(new SwarmSendRequest(From: "claude-opus", To: "codex", Topic: "old", Body: "Already acked"));
        store.AckMessage(msgAcked.Id, "codex");
        store.SendMessage(new SwarmSendRequest(From: "codex", To: "claude-opus", Topic: "other", Body: "Not for codex"));

        var start = await store.StartWorkAsync(new SwarmStartWorkRequest(
            Agent: "codex",
            Paths: ["src/Core", "src/Mcp"],
            PrNumber: 56));

        Assert.True(start.Ok);
        Assert.Equal("default", start.WorkspaceId);
        Assert.Equal(["src/Core", "src/Mcp"], start.ClaimedPaths);
        Assert.Empty(start.Conflicts);
        Assert.Equal(4, start.Locks.Count);

        var foreign = Assert.Single(start.ForeignLocks);
        Assert.Equal("claude-opus", foreign.Agent);
        Assert.Equal("docs/wiki", foreign.PathPattern);

        Assert.Equal(2, start.UnreadMessages.Count);
        Assert.Contains(start.UnreadMessages, m => m.Id == msgCodex.Id);
        Assert.Contains(start.UnreadMessages, m => m.Id == msgAll.Id);

        // Conflicting StartWork returns Ok = false, Conflicts, ForeignLocks, and still returns unread messages.
        var conflicted = store.StartWork(new SwarmStartWorkRequest(
            Agent: "antigravity-gemini",
            Paths: ["src/Core/Sub.cs"],
            PrNumber: 57));

        Assert.False(conflicted.Ok);
        Assert.Empty(conflicted.ClaimedPaths);
        Assert.Single(conflicted.Conflicts);
        Assert.Equal("codex", conflicted.Conflicts[0].ConflictingAgent);
        Assert.Equal(4, conflicted.ForeignLocks.Count);
        Assert.Single(conflicted.UnreadMessages); // receives msgAll

        // Empty paths StartWorkAsync succeeds cleanly without claiming new locks.
        var noLockStart = await store.StartWorkAsync(new SwarmStartWorkRequest(Agent: "claude-opus", Paths: []));
        Assert.True(noLockStart.Ok);
        Assert.Empty(noLockStart.ClaimedPaths);
        Assert.Equal(2, noLockStart.UnreadMessages.Count); // msgAll + msg for claude-opus
        Assert.Equal(3, noLockStart.ForeignLocks.Count); // codex's 3 locks
    }

    [Fact]
    public async Task WaitForMessagesAsyncWakesPromptlyOnConcurrentInsert()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();
        await store.EnsureInitializedAsync();

        var baseline = await store.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "old",
            Body: "Baseline"));
        await store.AckMessageAsync(baseline.Id, "codex");

        var sw = Stopwatch.StartNew();
        var waitTask = store.WaitForMessagesAsync(new SwarmWaitRequest(
            Agent: "codex",
            SinceId: baseline.Id,
            TimeoutMs: 10_000,
            Limit: 10));

        _ = Task.Run(async () =>
        {
            await Task.Delay(75);
            var writer = new SwarmBusStore(fx.DbPath);
            await writer.SendMessageAsync(new SwarmSendRequest(
                From: "claude-opus",
                To: "codex",
                Topic: "wakeup",
                Body: "New work ready"));
        });

        var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(10));
        sw.Stop();

        Assert.True(result.Ok);
        Assert.False(result.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(5000), $"Expected < 5000ms wakeup, elapsed {sw.Elapsed.TotalMilliseconds:0}ms");
        var msg = Assert.Single(result.Messages);
        Assert.Equal("wakeup", msg.Topic);
        Assert.Equal(msg.Id, result.LatestId);
    }

    [Fact]
    public async Task WaitForMessagesAsyncPaginatesBurstInAscendingCursorOrder()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var sentIds = new List<long>();
        for (var i = 1; i <= 10; i++)
        {
            var sent = await store.SendMessageAsync(new SwarmSendRequest(
                From: "claude-opus",
                To: "codex",
                Topic: "burst",
                Body: $"Message {i}"));
            sentIds.Add(sent.Id);
        }

        var batch1 = await store.WaitForMessagesAsync(new SwarmWaitRequest(
            Agent: "codex",
            SinceId: 0,
            TimeoutMs: 1000,
            Limit: 4));
        Assert.True(batch1.Ok);
        Assert.False(batch1.TimedOut);
        Assert.True(batch1.HasMore);
        Assert.Equal(sentIds[..4], batch1.Messages.Select(m => m.Id).ToArray());
        Assert.Equal(sentIds[3], batch1.LatestId);

        var batch2 = await store.WaitForMessagesAsync(new SwarmWaitRequest(
            Agent: "codex",
            SinceId: batch1.LatestId,
            TimeoutMs: 1000,
            Limit: 4));
        Assert.True(batch2.Ok);
        Assert.False(batch2.TimedOut);
        Assert.True(batch2.HasMore);
        Assert.Equal(sentIds[4..8], batch2.Messages.Select(m => m.Id).ToArray());
        Assert.Equal(sentIds[7], batch2.LatestId);

        var batch3 = await store.WaitForMessagesAsync(new SwarmWaitRequest(
            Agent: "codex",
            SinceId: batch2.LatestId,
            TimeoutMs: 1000,
            Limit: 4));
        Assert.True(batch3.Ok);
        Assert.False(batch3.TimedOut);
        Assert.False(batch3.HasMore);
        Assert.Equal(sentIds[8..10], batch3.Messages.Select(m => m.Id).ToArray());
        Assert.Equal(sentIds[9], batch3.LatestId);
    }

    [Fact]
    public async Task WaitForMessagesAsyncTimesOutCleanlyAndRespectsMentionsOnly()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var otherMsg = await store.SendMessageAsync(new SwarmSendRequest(
            From: "codex",
            To: "claude-opus",
            Topic: "private",
            Body: "Only for claude-opus"));

        // MentionsOnly = true on '*' must NOT return claude-opus's private message.
        var waitResult = await store.WaitForMessagesAsync(new SwarmWaitRequest(
            Agent: "*",
            SinceId: 0,
            MentionsOnly: true,
            TimeoutMs: 180));

        Assert.True(waitResult.Ok);
        Assert.True(waitResult.TimedOut);
        Assert.Empty(waitResult.Messages);
        Assert.Equal(0, waitResult.LatestId);

        // Immediate match returns without waiting when unread message exists for recipient > SinceId.
        var immediate = await store.WaitForMessagesAsync(new SwarmWaitRequest(
            Agent: "claude-opus",
            SinceId: 0,
            TimeoutMs: 10_000));

        Assert.True(immediate.Ok);
        Assert.False(immediate.TimedOut);
        Assert.Equal(otherMsg.Id, immediate.LatestId);
        Assert.Single(immediate.Messages);
    }
}
