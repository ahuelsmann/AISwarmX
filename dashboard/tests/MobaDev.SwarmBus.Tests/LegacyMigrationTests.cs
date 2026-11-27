using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using MobaDev.SwarmBus.Core.Contracts;
using MobaDev.SwarmBus.Core.Store;
using Xunit;

namespace MobaDev.SwarmBus.Tests;

[Collection("EnvVarTests")]
public sealed class LegacyMigrationTests
{
    [Fact]
    public void DefaultDatabasePathUsesV2OverrideAndRejectsProtectedLegacyPaths()
    {
        using var fx = new TempBusFixture();
        var originalLegacyEnv = Environment.GetEnvironmentVariable("MOBADEV_SWARM_BUS_DB");
        var originalV2Env = Environment.GetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB");
        try
        {
            var nodeLiveDb = Path.Combine(fx.RootPath, "node-live", "swarm-bus.db");
            var customV2Db = Path.Combine(fx.RootPath, "custom", "swarm-bus-v2.db");

            // When MOBADEV_SWARM_BUS_DB is set for Node MCP, C# ResolveDefaultDbPath still uses swarm-bus-v2.db.
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_DB", nodeLiveDb);
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", null);

            var defaultPath = SwarmBusStore.ResolveDefaultDbPath();
            var legacyPath = SwarmBusStore.ResolveLegacyDefaultDbPath();
            Assert.EndsWith("swarm-bus-v2.db", defaultPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("swarm-bus.db", legacyPath, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Path.GetFullPath(nodeLiveDb), defaultPath, StringComparer.OrdinalIgnoreCase);

            // Setting MOBADEV_SWARM_BUS_V2_DB overrides C# default path.
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", customV2Db);
            Assert.Equal(Path.GetFullPath(customV2Db), SwarmBusStore.ResolveDefaultDbPath());

            // Writable store must reject both default legacy path and MOBADEV_SWARM_BUS_DB path.
            Assert.Throws<InvalidOperationException>(() => new SwarmBusStore(legacyPath));
            Assert.Throws<InvalidOperationException>(() => new SwarmBusStore(nodeLiveDb.ToUpperInvariant() + Path.DirectorySeparatorChar));

            // Pointing MOBADEV_SWARM_BUS_V2_DB at MOBADEV_SWARM_BUS_DB must also be rejected.
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", nodeLiveDb);
            Assert.Throws<InvalidOperationException>(() => new SwarmBusStore());

            // ImportFromLegacyDb rejects importing onto the target DB itself.
            var v2Store = new SwarmBusStore(customV2Db);
            v2Store.EnsureInitialized();
            Assert.Throws<ArgumentException>(() => v2Store.ImportFromLegacyDb(customV2Db));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_DB", originalLegacyEnv);
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", originalV2Env);
        }
    }

    [Fact]
    public void ImportFromLegacyPreV3140DatabaseIsStrictlyReadOnlyAndZeroDataLoss()
    {
        using var fx = new TempBusFixture();
        var legacyPath = Path.Combine(fx.RootPath, "swarm-bus.db");
        var v2Path = Path.Combine(fx.RootPath, "swarm-bus-v2.db");
        var createdIso = "2026-11-24T10:00:00.000Z";
        var futureIso = "2099-11-24T10:00:00.000Z";

        TempBusFixture.ExecuteRawSql(legacyPath, $"""
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

        var legacyHashBefore = ComputeFileSha256(legacyPath);

        var store = new SwarmBusStore(v2Path);
        var imported = store.ImportFromLegacyDb(legacyPath);

        Assert.Equal(1, imported.MessagesImported);
        Assert.Equal(1, imported.LocksImported);
        Assert.Equal(0, imported.ReviewClaimsImported);
        Assert.Equal(0, imported.WorkQueueItemsImported);

        // Source legacy DB must remain byte-for-byte untouched (no WAL/schema mutation).
        Assert.Equal(legacyHashBefore, ComputeFileSha256(legacyPath));
        Assert.False(File.Exists(legacyPath + "-wal"));

        // Existing message & lock preserved under 'default' workspace in v2 DB.
        var inbox = store.GetInbox("antigravity-gemini", unreadOnly: true);
        var legacyMsg = Assert.Single(inbox.Messages);
        Assert.Equal("Pre-migration message", legacyMsg.Body);
        Assert.Equal("default", legacyMsg.WorkspaceId);

        var defaultLocks = store.ListLocks("default");
        var legacyLock = Assert.Single(defaultLocks);
        Assert.Equal("AGENTS.md", legacyLock.PathPattern);

        // Multi-workspace lock in v2 DB succeeds while legacy DB remains single-repo compatible.
        var flowLock = store.ClaimLocks(new LockClaimRequest(
            Agent: "claude-opus",
            Paths: ["AGENTS.md"],
            PrNumber: 901,
            WorkspaceId: "mobaflow"));
        Assert.True(flowLock.Ok);

        // Re-importing is idempotent.
        var reimported = store.ImportFromLegacyDb(legacyPath);
        Assert.Equal(0, reimported.MessagesImported);
        Assert.Equal(0, reimported.LocksImported);
    }

    [Fact]
    public async Task ImportFromLegacyV3140DatabaseNormalizesOutOfSetStatusesWithoutFailingCheckConstraints()
    {
        using var fx = new TempBusFixture();
        var legacyPath = Path.Combine(fx.RootPath, "legacy-v314.db");
        var v2Path = Path.Combine(fx.RootPath, "swarm-bus-v2.db");
        var nowIso = "2026-11-27T08:00:00.000Z";

        TempBusFixture.ExecuteRawSql(legacyPath, $"""
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
            CREATE TABLE meta (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL,
              updated_at_utc TEXT NOT NULL
            );
            INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status, wake_state)
            VALUES ('{nowIso}', 'watchdog', 'codex', 'ci', 'body', 'weird_status', 'corrupt_wake');
            INSERT INTO review_claims (pr_number, head_sha, role, agent, claimed_at_utc, status)
            VALUES (58, '0123456789ABCDEF0123456789ABCDEF01234567', 'final', 'claude-opus', '{nowIso}', 'stale_unknown');
            INSERT INTO review_claims (pr_number, head_sha, role, agent, claimed_at_utc, status)
            VALUES (59, 'not-a-valid-sha', 'final', 'claude-opus', '{nowIso}', 'claimed');
            INSERT INTO work_queue (issue_number, title, priority, risk_class, status, updated_at_utc)
            VALUES (58, 'Unknown queue status', 1, 'M', 'Planned', '{nowIso}');
            INSERT INTO work_queue (issue_number, title, priority, risk_class, status, updated_at_utc)
            VALUES (59, 'Completed alias', 1, 'L', 'Completed', '{nowIso}');
            INSERT INTO meta (key, value, updated_at_utc)
            VALUES ('code_wip', '1', '{nowIso}');
            """);

        var legacyHashBefore = ComputeFileSha256(legacyPath);

        var store = new SwarmBusStore(v2Path);
        var result = await store.ImportFromLegacyDbAsync(legacyPath);

        Assert.Equal(1, result.MessagesImported);
        Assert.Equal(1, result.ReviewClaimsImported); // invalid SHA row skipped safely
        Assert.Equal(2, result.WorkQueueItemsImported);
        Assert.Equal(1, result.MetaEntriesImported);
        Assert.Equal(legacyHashBefore, ComputeFileSha256(legacyPath));

        var importedMsg = Assert.Single(store.GetInbox("codex", unreadOnly: false).Messages);
        Assert.Equal("unread", importedMsg.Status);
        Assert.Null(importedMsg.WakeState);

        var importedClaim = Assert.Single(store.ReviewClaim(new ReviewClaimRequest(Action: "list", PrNumber: 58)).Claims!);
        Assert.Equal("released", importedClaim.Status);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", importedClaim.HeadSha);

        var queue = store.QueueList();
        Assert.Equal(1, queue.CodeWip);
        Assert.Equal("queued", queue.Items.Single(i => i.IssueNumber == 58).Status);
        Assert.Equal("done", queue.Items.Single(i => i.IssueNumber == 59).Status);

        // Same issue number in another workspace succeeds in v2.
        var flowUpsert = store.QueueUpsert(new WorkQueueUpsertRequest(
            IssueNumber: 58,
            Title: "MOBAflow #58",
            Priority: 1,
            WorkspaceId: "mobaflow"));
        Assert.True(flowUpsert.Ok);
    }

    [Fact]
    public void DirectoryJunctionOrSymlinkAliasToProtectedLegacyDbIsRejected()
    {
        using var fx = new TempBusFixture();
        var originalLegacyEnv = Environment.GetEnvironmentVariable("MOBADEV_SWARM_BUS_DB");
        var originalV2Env = Environment.GetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB");

        var realLegacyDir = Path.Combine(fx.RootPath, "real-legacy");
        var junctionDir = Path.Combine(fx.RootPath, "junction-legacy");
        var realV2Dir = Path.Combine(fx.RootPath, "real-v2");
        var junctionV2Dir = Path.Combine(fx.RootPath, "junction-v2");
        Directory.CreateDirectory(realLegacyDir);
        Directory.CreateDirectory(realV2Dir);

        var realLegacyDb = Path.Combine(realLegacyDir, "swarm-bus.db");
        TempBusFixture.ExecuteRawSql(realLegacyDb, """
            CREATE TABLE messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_at_utc TEXT NOT NULL,
              sender TEXT NOT NULL,
              recipient TEXT NOT NULL,
              topic TEXT NOT NULL,
              body TEXT NOT NULL
            );
            """);

        CreateDirectoryJunctionOrSymlink(junctionDir, realLegacyDir);
        CreateDirectoryJunctionOrSymlink(junctionV2Dir, realV2Dir);

        try
        {
            var junctionedLegacyDb = Path.Combine(junctionDir, "swarm-bus.db");
            Assert.Equal(
                SwarmBusStore.ResolveCanonicalPhysicalPath(realLegacyDb),
                SwarmBusStore.ResolveCanonicalPhysicalPath(junctionedLegacyDb),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            // Protect realLegacyDb via MOBADEV_SWARM_BUS_DB, then try opening via junction alias.
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_DB", realLegacyDb);
            Assert.Throws<InvalidOperationException>(() => new SwarmBusStore(junctionedLegacyDb));

            // Also reject MOBADEV_SWARM_BUS_V2_DB pointing through the junction alias.
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", junctionedLegacyDb);
            Assert.Throws<InvalidOperationException>(() => new SwarmBusStore());

            // Also reject self-import when source and target resolve to the same physical file via a junction.
            var realV2Db = Path.Combine(realV2Dir, "swarm-bus-v2.db");
            var junctionedV2Db = Path.Combine(junctionV2Dir, "swarm-bus-v2.db");
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", null);
            var v2Store = new SwarmBusStore(realV2Db);
            v2Store.EnsureInitialized();
            Assert.Throws<ArgumentException>(() => v2Store.ImportFromLegacyDb(junctionedV2Db));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_DB", originalLegacyEnv);
            Environment.SetEnvironmentVariable("MOBADEV_SWARM_BUS_V2_DB", originalV2Env);
            DeleteDirectoryLinkIfExists(junctionDir);
            DeleteDirectoryLinkIfExists(junctionV2Dir);
        }
    }

    [Fact]
    public void LinkResolutionFailureOnExistingSegmentFailsClosedWithoutOpeningDatabase()
    {
        using var fx = new TempBusFixture();
        var existingSegmentDir = Path.Combine(fx.RootPath, "protected-dir");
        Directory.CreateDirectory(existingSegmentDir);

        var sentinelDbPath = Path.Combine(existingSegmentDir, "sentinel.db");
        File.WriteAllBytes(sentinelDbPath, [1, 2, 3, 4]);
        var originalHash = ComputeFileSha256(sentinelDbPath);

        var legacyPath = Path.Combine(fx.RootPath, "other-legacy", "swarm-bus.db");

        // Permission or general I/O failure on an existing path segment must fail closed (propagate), never fall back to lexical equality.
        Assert.Throws<UnauthorizedAccessException>(() => SwarmBusStore.ValidateTargetNotProtectedLegacyPath(
            sentinelDbPath,
            legacyPath,
            info => string.Equals(info.FullName, existingSegmentDir, StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("Access denied reading reparse point.")
                : null));

        Assert.Throws<IOException>(() => SwarmBusStore.ValidateTargetNotProtectedLegacyPath(
            sentinelDbPath,
            legacyPath,
            info => string.Equals(info.FullName, existingSegmentDir, StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("Unreadable reparse buffer.")
                : null));

        Assert.Equal(originalHash, ComputeFileSha256(sentinelDbPath));
    }

    private static void CreateDirectoryJunctionOrSymlink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            proc.WaitForExit(5000);
            Assert.Equal(0, proc.ExitCode);
            return;
        }

        Directory.CreateSymbolicLink(linkPath, targetPath);
    }

    private static void DeleteDirectoryLinkIfExists(string linkPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath, recursive: false);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
