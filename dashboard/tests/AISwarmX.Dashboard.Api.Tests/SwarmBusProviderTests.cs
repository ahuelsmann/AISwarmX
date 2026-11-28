using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Providers;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

public sealed class SwarmBusProviderTests
{
    [Fact]
    public async Task ReturnsEmptyOkWhenSwarmBusDbDoesNotExist()
    {
        using var fx = new TempDashboardFixture();
        var missingDb = Path.Combine(fx.RootPath, "missing", "swarm-bus.db");
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath,
            fx.MobaFlowPath,
            fx.ClaudeHomePath,
            fx.CodexHomePath,
            fx.AntigravityHomePath,
            fx.GeminiConfigPath,
            string.Empty,
            missingDb);

        var provider = new SwarmBusProvider();
        var result = await provider.GetSwarmBusAsync(paths, CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.Equal(0, result.UnreadCount);
        Assert.Equal(0, result.ActiveLockCount);
        Assert.Empty(result.RecentMessages);
        Assert.Empty(result.ActiveLocks);
    }

    [Fact]
    public async Task ReadsMessagesAndFiltersExpiredLocksReadOnly()
    {
        using var fx = new TempDashboardFixture();
        var dbPath = Path.Combine(fx.RootPath, "MOBAdev", "swarm-bus.db");
        var now = DateTimeOffset.UtcNow;
        var futureIso = now.AddMinutes(30).ToString("O");
        var pastIso = now.AddMinutes(-10).ToString("O");
        var createdIso = now.AddMinutes(-1).ToString("O");

        TempDashboardFixture.CreateSqliteDb(dbPath, $"""
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
            VALUES ('{createdIso}', 'antigravity-gemini', 'codex', 'review-pass', 'P3 PASS on PR #541', 541, 'high', 'unread');
            INSERT INTO messages (created_at_utc, sender, recipient, topic, body, pr_number, priority, status, acked_by, acked_at_utc)
            VALUES ('{createdIso}', 'watchdog', 'claude-opus', 'CiOrSonarFailed', 'Sonar S6505 on PR #537', 537, 'high', 'acked', 'claude-opus', '{createdIso}');
            INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
            VALUES ('codex', 'MOBAdemo/app/src/shell', 541, '{createdIso}', '{futureIso}');
            INSERT INTO locks (agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
            VALUES ('claude-opus', 'expired/path', 536, '{pastIso}', '{pastIso}');
            """);

        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath,
            fx.MobaFlowPath,
            fx.ClaudeHomePath,
            fx.CodexHomePath,
            fx.AntigravityHomePath,
            fx.GeminiConfigPath,
            string.Empty,
            dbPath);

        var provider = new SwarmBusProvider();
        var result = await provider.GetSwarmBusAsync(paths, CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.Equal(1, result.UnreadCount);
        Assert.Equal(2, result.RecentMessages.Count);
        Assert.Equal("watchdog", result.RecentMessages[0].Sender);
        Assert.Equal("antigravity-gemini", result.RecentMessages[1].Sender);
        Assert.Single(result.ActiveLocks);
        Assert.Equal("MOBAdemo/app/src/shell", result.ActiveLocks[0].PathPattern);
        Assert.Null(result.RecentMessages[0].WorkspaceId);
    }

    [Fact]
    public async Task ReadsWorkspaceIdFromV2DatabaseWhenColumnExists()
    {
        using var fx = new TempDashboardFixture();
        var dbPath = Path.Combine(fx.RootPath, "MOBAdev", "swarm-bus-v2.db");
        var now = DateTimeOffset.UtcNow;
        var futureIso = now.AddMinutes(30).ToString("O");
        var createdIso = now.AddMinutes(-1).ToString("O");

        TempDashboardFixture.CreateSqliteDb(dbPath, $"""
            CREATE TABLE messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              workspace_id TEXT NOT NULL DEFAULT 'default',
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
              workspace_id TEXT NOT NULL DEFAULT 'default',
              agent TEXT NOT NULL,
              path_pattern TEXT NOT NULL,
              pr_number INTEGER,
              claimed_at_utc TEXT NOT NULL,
              expires_at_utc TEXT NOT NULL
            );
            INSERT INTO messages (workspace_id, created_at_utc, sender, recipient, topic, body, status)
            VALUES ('sentix', '{createdIso}', 'antigravity-gemini', 'codex', 'p2-sentix', 'SentiX msg', 'unread');
            INSERT INTO locks (workspace_id, agent, path_pattern, pr_number, claimed_at_utc, expires_at_utc)
            VALUES ('aiswarmx', 'codex', 'dashboard/src', 58, '{createdIso}', '{futureIso}');
            """);

        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath,
            fx.MobaFlowPath,
            fx.ClaudeHomePath,
            fx.CodexHomePath,
            fx.AntigravityHomePath,
            fx.GeminiConfigPath,
            string.Empty,
            dbPath);

        var provider = new SwarmBusProvider();
        var result = await provider.GetSwarmBusAsync(paths, CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.Equal("sentix", Assert.Single(result.RecentMessages).WorkspaceId);
        Assert.Equal("aiswarmx", Assert.Single(result.ActiveLocks).WorkspaceId);
    }
}
