using MobaDev.SwarmBus.Core.Contracts;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

public sealed class LegacyMigrationTests
{
    [Fact]
    public void MigratesPreV3140DatabaseWithZeroDataLossAndEnablesMultiWorkspace()
    {
        using var fx = new TempBusFixture();
        var createdIso = "2026-11-24T10:00:00.000Z";
        var futureIso = "2099-11-24T10:00:00.000Z";

        // Legacy pre-v3.14.0 schema: no state/wake/workspace_id, locks has global UNIQUE(path_pattern),
        // no review_claims, no work_queue, no meta.
        TempBusFixture.ExecuteRawSql(fx.DbPath, $"""
            CREATE TABLE messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_at_utc TEXT NOT NULL,
              sender TEXT NOT NULL,
              recipient TEXT NOT NULL,
              topic TEXT NOT NULL,
              body TEXT NOT NULL,
              pr_number INTEGER,
              issue_number INTEGER,
              priority TEXT NOT NULL DEFAULT 'normal',
              reply_to_id INTEGER,
              status TEXT NOT NULL DEFAULT 'unread',
              acked_by TEXT,
              acked_at_utc TEXT
            );
            CREATE TABLE locks (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              agent TEXT NOT NULL,
              path_pattern TEXT NOT NULL UNIQUE,
              pr_number INTEGER,
              claimed_at_utc TEXT NOT NULL,
              expires_at_utc TEXT NOT NULL
            );
            INSERT INTO messages (created_at_utc, sender, recipient, topic, body, pr_number, priority, status)
            VALUES ('{createdIso}', 'codex', 'antigravity-gemini', 'legacy-msg', 'Pre-migration message', 500, 'high', 'unread');
            INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
            VALUES ('codex', 'AGENTS.md', 500, '{createdIso}', '{futureIso}');
            """);

        var store = fx.CreateStore();
        store.EnsureInitialized();

        // Existing message preserved under 'default' workspace.
        var inbox = store.GetInbox("antigravity-gemini", unreadOnly: true);
        var legacyMsg = Assert.Single(inbox.Messages);
        Assert.Equal("Pre-migration message", legacyMsg.Body);
        Assert.Equal("default", legacyMsg.WorkspaceId);
        Assert.Null(legacyMsg.State);

        // Existing lock preserved under 'default' workspace.
        var defaultLocks = store.ListLocks("default");
        var legacyLock = Assert.Single(defaultLocks);
        Assert.Equal("AGENTS.md", legacyLock.PathPattern);
        Assert.Equal("codex", legacyLock.Agent);

        // New multi-workspace lock on AGENTS.md succeeds because legacy UNIQUE(path_pattern) was upgraded.
        var flowLock = store.ClaimLocks(new LockClaimRequest(
            Agent: "claude-opus",
            Paths: ["AGENTS.md"],
            PrNumber: 901,
            WorkspaceId: "mobaflow"));
        Assert.True(flowLock.Ok);

        // New v3.14.0 features (review_claims, work_queue, meta, state dedup) work seamlessly.
        var claim = store.ReviewClaim(new ReviewClaimRequest(
            Action: "claim",
            Agent: "claude-opus",
            PrNumber: 500,
            HeadSha: "0123456789abcdef0123456789abcdef01234567",
            Role: "final"));
        Assert.True(claim.Claimed);

        store.QueueUpsert(new WorkQueueUpsertRequest(IssueNumber: 56, Title: "Migrated queue", Priority: 1));
        var next = store.QueueNext("codex");
        Assert.Equal(56, next.Item!.IssueNumber);
    }

    [Fact]
    public void MigratesV3140SingleRepoSchemaToMultiWorkspaceWithZeroDataLoss()
    {
        using var fx = new TempBusFixture();
        var nowIso = "2026-11-27T08:00:00.000Z";

        TempBusFixture.ExecuteRawSql(fx.DbPath, $"""
            CREATE TABLE messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_at_utc TEXT NOT NULL,
              sender TEXT NOT NULL,
              recipient TEXT NOT NULL,
              topic TEXT NOT NULL,
              body TEXT NOT NULL,
              pr_number INTEGER,
              issue_number INTEGER,
              priority TEXT NOT NULL DEFAULT 'normal',
              reply_to_id INTEGER,
              status TEXT NOT NULL DEFAULT 'unread',
              acked_by TEXT,
              acked_at_utc TEXT,
              state TEXT,
              wake_ok INTEGER,
              wake_state TEXT,
              wake_attempt_expires_at_utc TEXT
            );
            CREATE TABLE locks (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              agent TEXT NOT NULL,
              path_pattern TEXT NOT NULL UNIQUE,
              pr_number INTEGER,
              claimed_at_utc TEXT NOT NULL,
              expires_at_utc TEXT NOT NULL
            );
            CREATE TABLE review_claims (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              pr_number INTEGER NOT NULL,
              head_sha TEXT NOT NULL,
              role TEXT NOT NULL,
              agent TEXT NOT NULL,
              claimed_at_utc TEXT NOT NULL,
              status TEXT NOT NULL DEFAULT 'claimed',
              expires_at_utc TEXT,
              completed_at_utc TEXT,
              UNIQUE(pr_number, head_sha, role)
            );
            CREATE TABLE work_queue (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              issue_number INTEGER UNIQUE NOT NULL,
              title TEXT NOT NULL,
              priority INTEGER NOT NULL,
              risk_class TEXT,
              status TEXT NOT NULL,
              claimed_by TEXT,
              claimed_at_utc TEXT,
              lease_expires_utc TEXT,
              updated_at_utc TEXT NOT NULL
            );
            CREATE INDEX idx_work_queue_status_priority ON work_queue (status, priority ASC, id ASC);
            CREATE TABLE meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL,
              updated_at_utc TEXT NOT NULL
            );
            INSERT INTO review_claims (pr_number, head_sha, role, agent, claimed_at_utc, status, completed_at_utc)
            VALUES (58, '0123456789abcdef0123456789abcdef01234567', 'final', 'claude-opus', '{nowIso}', 'completed', '{nowIso}');
            INSERT INTO work_queue (issue_number, title, priority, risk_class, status, updated_at_utc)
            VALUES (58, 'Existing v3.14 item', 1, 'M', 'queued', '{nowIso}');
            INSERT INTO meta (key, value, updated_at_utc)
            VALUES ('code_wip', '1', '{nowIso}');
            """);

        var store = fx.CreateStore();
        store.EnsureInitialized();

        var claims = store.ReviewClaim(new ReviewClaimRequest(Action: "list", PrNumber: 58));
        Assert.Single(claims.Claims!);
        Assert.Equal("completed", claims.Claims![0].Status);

        var queue = store.QueueList();
        Assert.Equal(1, queue.CodeWip);
        var item = Assert.Single(queue.Items);
        Assert.Equal(58, item.IssueNumber);

        // Same issue number in another workspace must succeed post-migration.
        var flowUpsert = store.QueueUpsert(new WorkQueueUpsertRequest(
            IssueNumber: 58,
            Title: "MOBAflow #58",
            Priority: 1,
            WorkspaceId: "mobaflow"));
        Assert.True(flowUpsert.Ok);
    }
}
