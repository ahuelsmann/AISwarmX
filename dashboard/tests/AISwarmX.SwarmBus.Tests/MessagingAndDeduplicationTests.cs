using AISwarmX.SwarmBus.Core.Contracts;
using AISwarmX.SwarmBus.Core.Store;
using Xunit;

namespace AISwarmX.SwarmBus.Tests;

public sealed class MessagingAndDeduplicationTests
{
    [Fact]
    public async Task SendInboxAckAndReplyRoundTripWorks()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var sent = await store.SendMessageAsync(new SwarmSendRequest(
            From: "antigravity-gemini",
            To: "codex",
            Topic: "review-pass",
            Body: "PR #541 P3 UI/UX PASS",
            PrNumber: 541,
            Priority: "high"));

        Assert.True(sent.Ok);
        Assert.Equal(1, sent.Id);
        Assert.Equal("default", sent.WorkspaceId);

        var inbox = store.GetInbox("codex", unreadOnly: true);
        Assert.Equal(1, inbox.Count);
        Assert.Equal("review-pass", inbox.Messages[0].Topic);
        Assert.Equal("high", inbox.Messages[0].Priority);

        var ack = await store.AckMessageAsync(sent.Id, "codex", "Fixing polish notes now.");
        Assert.True(ack.Ok);
        Assert.Equal(sent.Id, ack.AckedMessageId);
        Assert.NotNull(ack.ReplyMessage);
        Assert.Equal("antigravity-gemini", ack.ReplyMessage.To);

        Assert.Equal(0, store.GetInbox("codex", unreadOnly: true).Count);
        var geminiInbox = store.GetInbox("antigravity-gemini", unreadOnly: true);
        Assert.Equal(1, geminiInbox.Count);
        Assert.Equal("re:review-pass", geminiInbox.Messages[0].Topic);
        Assert.Equal(sent.Id, geminiInbox.Messages[0].ReplyToId);
    }

    [Theory]
    [InlineData("watchdog")]
    [InlineData("claude-opus-worker")]
    [InlineData("system")]
    public void HeadlessRecipientsAreAutoAcked(string headlessRecipient)
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var sent = store.SendMessage(new SwarmSendRequest(
            From: "codex",
            To: headlessRecipient,
            Topic: "status-note",
            Body: "Done"));

        Assert.Equal(0, store.GetInbox("all", unreadOnly: true).Count);
        var allMessages = store.GetInbox(headlessRecipient, unreadOnly: false);
        Assert.Single(allMessages.Messages);
        Assert.Equal("acked", allMessages.Messages[0].Status);
        Assert.Equal("auto-headless", allMessages.Messages[0].AckedBy);
        Assert.Equal(sent.Id, allMessages.Messages[0].Id);
    }

    [Fact]
    public void WatchdogSupersedesOlderUnreadAlertsForSamePr()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var first = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "First failure",
            PrNumber: 545));

        var second = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "Now green",
            PrNumber: 545));

        // Different PR should not be superseded.
        var otherPr = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "PR 546 failing",
            PrNumber: 546));

        var unread = store.GetInbox("codex", unreadOnly: true);
        Assert.Equal(2, unread.Count);
        Assert.DoesNotContain(unread.Messages, m => m.Id == first.Id);
        Assert.Contains(unread.Messages, m => m.Id == second.Id);
        Assert.Contains(unread.Messages, m => m.Id == otherPr.Id);

        var all = store.GetInbox("codex", unreadOnly: false);
        var firstMsg = all.Messages.Single(m => m.Id == first.Id);
        Assert.Equal("acked", firstMsg.Status);
        Assert.Equal("auto-superseded", firstMsg.AckedBy);
    }

    [Fact]
    public void StateDeduplicationIsAtomicAndRecipientScoped()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        var m1 = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "Failed at sha1",
            PrNumber: 550,
            State: "failed:sha1"));
        Assert.False(m1.Deduplicated);

        // Same sender/recipient/topic/pr/state -> deduplicated.
        var m2 = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "Failed at sha1 again",
            PrNumber: 550,
            State: "failed:sha1"));
        Assert.True(m2.Deduplicated);
        Assert.Equal(m1.Id, m2.Id);

        // Different recipient with identical state -> NOT deduplicated (recipient-scoped!).
        var m3 = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "claude-opus",
            Topic: "CiOrSonarFailed",
            Body: "Failed at sha1",
            PrNumber: 550,
            State: "failed:sha1"));
        Assert.False(m3.Deduplicated);
        Assert.NotEqual(m1.Id, m3.Id);

        // State changes -> new message inserted.
        var m4 = store.SendMessage(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "Failed at sha2",
            PrNumber: 550,
            State: "failed:sha2"));
        Assert.False(m4.Deduplicated);
        Assert.NotEqual(m1.Id, m4.Id);
    }

    [Fact]
    public async Task WakeReservationBlocksDuplicateWakeWithin120sAndRetriesAfterExpiry()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var wakeAttempts = 0;

        // First store leaves wake pending (no dispatcher) to test atomic 120s pending reservation.
        var storeNoDispatcher = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        var initial = await storeNoDispatcher.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "PR #560 green",
            PrNumber: 560,
            State: "green:sha1",
            Wake: true));

        Assert.False(initial.Deduplicated);
        Assert.Equal("pending", initial.WakeState);

        var msgRow = storeNoDispatcher.GetInbox("codex").Messages.Single();
        Assert.Equal("pending", msgRow.WakeState);
        Assert.Equal(clock.GetUtcNow().AddSeconds(120), msgRow.WakeAttemptExpiresAtUtc);

        // Within 120s -> wake_in_progress=true, no retry.
        clock.Advance(TimeSpan.FromSeconds(60));
        var storeWithDispatcher = fx.CreateStore(new SwarmBusStoreOptions
        {
            TimeProvider = clock,
            WakeDispatcher = (ctx, _) =>
            {
                Interlocked.Increment(ref wakeAttempts);
                return Task.FromResult(new SwarmWakeResult(Attempted: true, Ok: true, Target: ctx.Recipient));
            }
        });

        var inFlight = await storeWithDispatcher.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "PR #560 green",
            PrNumber: 560,
            State: "green:sha1",
            Wake: true));

        Assert.True(inFlight.Deduplicated);
        Assert.True(inFlight.WakeInProgress);
        Assert.False(inFlight.WakeRetried);
        Assert.Equal(0, wakeAttempts);

        // After 120s expiry -> retry-wake executes and transitions wake_state to 'ok'.
        clock.Advance(TimeSpan.FromSeconds(61));
        var retried = await storeWithDispatcher.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "PR #560 green",
            PrNumber: 560,
            State: "green:sha1",
            Wake: true));

        Assert.True(retried.Deduplicated);
        Assert.True(retried.WakeRetried);
        Assert.Equal("ok", retried.WakeState);
        Assert.Equal(1, wakeAttempts);

        // Once wake_state is 'ok', subsequent matching state does not re-wake.
        clock.Advance(TimeSpan.FromMinutes(10));
        var alreadyWoken = await storeWithDispatcher.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "PR #560 green",
            PrNumber: 560,
            State: "green:sha1",
            Wake: true));

        Assert.True(alreadyWoken.Deduplicated);
        Assert.False(alreadyWoken.WakeRetried);
        Assert.Equal("ok", alreadyWoken.WakeState);
        Assert.Equal(1, wakeAttempts);
    }

    [Fact]
    public async Task FailedWakeSetsWakeStateFailedAndAllowsRetryOnNextEvent()
    {
        using var fx = new TempBusFixture();
        var shouldSucceed = false;
        var store = fx.CreateStore(new SwarmBusStoreOptions
        {
            WakeDispatcher = (ctx, _) => Task.FromResult(new SwarmWakeResult(
                Attempted: true,
                Ok: shouldSucceed,
                Target: ctx.Recipient,
                Error: shouldSucceed ? null : "codex queue unavailable"))
        });

        var first = await store.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "Sonar issue",
            PrNumber: 561,
            State: "sonar:sha1",
            Wake: true));
        Assert.Equal("failed", first.WakeState);

        shouldSucceed = true;
        var second = await store.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CiOrSonarFailed",
            Body: "Sonar issue",
            PrNumber: 561,
            State: "sonar:sha1",
            Wake: true));
        Assert.True(second.Deduplicated);
        Assert.True(second.WakeRetried);
        Assert.Equal("ok", second.WakeState);
    }

    [Fact]
    public void GetStatusReturnsWorkspaceScopedUnreadCountsAndActiveLocks()
    {
        using var fx = new TempBusFixture();
        var store = fx.CreateStore();

        store.SendMessage(new SwarmSendRequest(From: "codex", To: "claude-opus", Topic: "ping", Body: "msg 1", WorkspaceId: "mobadev"));
        store.SendMessage(new SwarmSendRequest(From: "codex", To: "claude-opus", Topic: "ping", Body: "msg 2", WorkspaceId: "mobadev"));
        store.SendMessage(new SwarmSendRequest(From: "codex", To: "antigravity-gemini", Topic: "ping", Body: "msg 3", WorkspaceId: "mobaflow"));
        store.ClaimLocks(new LockClaimRequest(Agent: "codex", Paths: ["src/Core"], PrNumber: 56, WorkspaceId: "mobadev"));

        var devStatus = store.GetStatus(limit: 10, workspaceId: "mobadev");
        Assert.Equal(2, devStatus.TotalUnread);
        Assert.Single(devStatus.UnreadByRecipient);
        Assert.Equal("claude-opus", devStatus.UnreadByRecipient[0].Recipient);
        Assert.Equal(2, devStatus.UnreadByRecipient[0].UnreadCount);
        Assert.Single(devStatus.ActiveLocks);

        var allStatus = store.GetStatus(limit: 10, workspaceId: "*");
        Assert.Equal(3, allStatus.TotalUnread);
        Assert.Equal(2, allStatus.UnreadByRecipient.Count);
    }

    [Fact]
    public async Task LateWakeCompletionCannotClobberNewerPendingOrOkWakeLease()
    {
        using var fx = new TempBusFixture();
        var clock = new TestTimeProvider();
        var storeNoDispatcher = fx.CreateStore(new SwarmBusStoreOptions { TimeProvider = clock });

        var first = await storeNoDispatcher.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "PR #600 green",
            PrNumber: 600,
            State: "green:sha1",
            Wake: true));
        var staleLease = storeNoDispatcher.GetInbox("codex").Messages.Single().WakeAttemptExpiresAtUtc!.Value;

        // Advance past 120s lease so a retry claims a fresh lease.
        clock.Advance(TimeSpan.FromSeconds(125));
        var retry = await storeNoDispatcher.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "AllChecksGreenReady",
            Body: "PR #600 green",
            PrNumber: 600,
            State: "green:sha1",
            Wake: true));
        Assert.True(retry.WakeRetried);

        // Stale worker finishing late with the old lease token must be fenced off.
        var staleApplied = storeNoDispatcher.CompleteWakeAttempt(first.Id, ok: false, staleLease);
        Assert.False(staleApplied);

        var currentMsg = storeNoDispatcher.GetInbox("codex").Messages.Single();
        Assert.Equal("pending", currentMsg.WakeState);
        var currentLease = currentMsg.WakeAttemptExpiresAtUtc!.Value;
        Assert.NotEqual(staleLease, currentLease);

        // Current holder with matching lease token succeeds.
        Assert.True(storeNoDispatcher.CompleteWakeAttempt(first.Id, ok: true, currentLease));
        Assert.Equal("ok", storeNoDispatcher.GetInbox("codex").Messages.Single().WakeState);
    }

    [Fact]
    public async Task WakeDispatchTimeoutAndCancellationTransitionWakeStateToFailed()
    {
        using var fx = new TempBusFixture();
        var timeoutStore = fx.CreateStore(new SwarmBusStoreOptions
        {
            WakeDispatchTimeout = TimeSpan.FromMilliseconds(80),
            WakeDispatcher = async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new SwarmWakeResult(Attempted: true, Ok: true, Target: ctx.Recipient);
            }
        });

        var timedOut = await timeoutStore.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "TimeoutCheck",
            Body: "Slow wake",
            PrNumber: 601,
            State: "timeout:1",
            Wake: true));

        Assert.Equal("failed", timedOut.WakeState);
        Assert.False(timedOut.WakeResults![0].Ok);
        Assert.Equal("timeout", timedOut.WakeResults[0].Reason);

        using var cts = new CancellationTokenSource();
        var cancelStore = fx.CreateStore(new SwarmBusStoreOptions
        {
            WakeDispatchTimeout = TimeSpan.FromSeconds(10),
            WakeDispatcher = async (ctx, ct) =>
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return new SwarmWakeResult(Attempted: true, Ok: true, Target: ctx.Recipient);
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelStore.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "CancelCheck",
            Body: "Cancelled wake",
            PrNumber: 602,
            State: "cancel:1",
            Wake: true), cts.Token));

        var msg = cancelStore.GetInbox("codex").Messages.Single(m => m.PrNumber == 602);
        Assert.Equal("failed", msg.WakeState);
        Assert.Null(msg.WakeAttemptExpiresAtUtc);
    }

    [Fact]
    public async Task NonCooperativeWakeDispatcherIgnoringCancellationStillTimesOutBoundedly()
    {
        using var fx = new TempBusFixture();
        var neverCompletes = new TaskCompletionSource<SwarmWakeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var store = fx.CreateStore(new SwarmBusStoreOptions
        {
            WakeDispatchTimeout = TimeSpan.FromMilliseconds(75),
            WakeDispatcher = (_, _) => neverCompletes.Task
        });

        var result = await store.SendMessageAsync(new SwarmSendRequest(
            From: "watchdog",
            To: "codex",
            Topic: "NonCooperativeTimeout",
            Body: "Hung dispatcher test",
            PrNumber: 603,
            State: "hang:1",
            Wake: true)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("failed", result.WakeState);
        Assert.False(result.WakeResults![0].Ok);
        Assert.Equal("timeout", result.WakeResults[0].Reason);

        var stored = store.GetInbox("codex").Messages.Single(m => m.PrNumber == 603);
        Assert.Equal("failed", stored.WakeState);
        Assert.Null(stored.WakeAttemptExpiresAtUtc);
    }
}
