using AISwarmX.SwarmBus.Core.Contracts;
using AISwarmX.SwarmBus.Core.Store;
using Xunit;

namespace AISwarmX.SwarmBus.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task ParallelFreshDbOpenAndConcurrentImmediateTransactionsSucceedWithoutDatabaseLocked()
    {
        using var fx = new TempBusFixture();
        const int workers = 16;
        using var barrier = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, workers).Select(i => Task.Run(async () =>
        {
            var store = new SwarmBusStore(fx.DbPath);
            barrier.Wait();

            // Simultaneous fresh-DB schema init + concurrent write transactions across independent store instances.
            var send = await store.SendMessageAsync(new SwarmSendRequest(
                From: $"worker-{i}",
                To: "codex",
                Topic: "parallel-ping",
                Body: $"Payload {i}",
                PrNumber: 700 + i,
                State: $"state-{i}"));

            var lockRes = await store.ClaimLocksAsync(new LockClaimRequest(
                Agent: $"worker-{i}",
                Paths: [$"src/Area{i}"],
                PrNumber: 700 + i));

            var upsertRes = await store.QueueUpsertAsync(new WorkQueueUpsertRequest(
                IssueNumber: 800 + i,
                Title: $"Task {i}",
                Priority: i % 3,
                RiskClass: "L"));

            var status = await store.GetStatusAsync(limit: 20);

            return (send.Ok, lockRes.Ok, upsertRes.Ok, status.ActiveLocks.Count);
        })).ToArray();

        barrier.Set();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r =>
        {
            Assert.True(r.Item1);
            Assert.True(r.Item2);
            Assert.True(r.Item3);
        });

        var verifier = fx.CreateStore();
        var finalStatus = verifier.GetStatus(limit: 50);
        Assert.Equal(workers, finalStatus.TotalUnread);
        Assert.Equal(workers, finalStatus.ActiveLocks.Count);
        Assert.Equal(workers, verifier.QueueList().Items.Count);
    }

    [Fact]
    public async Task AsyncBusyRetryExhaustionThrowsSqliteBusyWithoutBlockingIndefinitely()
    {
        using var fx = new TempBusFixture();
        var store = new SwarmBusStore(fx.DbPath, new SwarmBusStoreOptions { MaxBusyRetries = 2 });
        await store.EnsureInitializedAsync();

        var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = fx.DbPath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();

        await using var lockHolder = new Microsoft.Data.Sqlite.SqliteConnection(cs);
        await lockHolder.OpenAsync();
        await using (var beginCmd = lockHolder.CreateCommand())
        {
            beginCmd.CommandText = "BEGIN EXCLUSIVE;";
            await beginCmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            store.SendMessageAsync(new SwarmSendRequest(
                From: "codex",
                To: "claude-opus",
                Topic: "busy-test",
                Body: "Should exhaust retries cleanly")));

        Assert.Equal(5, ex.SqliteErrorCode);
    }
}
