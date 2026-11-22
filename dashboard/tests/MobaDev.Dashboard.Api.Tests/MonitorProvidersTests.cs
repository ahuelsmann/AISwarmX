using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Providers;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed class MonitorProvidersTests
{
    [Fact]
    public async Task ClaudeMonitorProvider_DetectsRateLimitWithoutLeakingChatText()
    {
        using var fx = new TempDashboardFixture();
        var sessionsDir = Path.Combine(fx.ClaudeHomePath, "sessions");
        Directory.CreateDirectory(sessionsDir);

        var sessionId = "11111111-1111-1111-1111-111111111111";
        var cwd = Path.Combine(fx.MobaFlowPath, "worktree-test");
        var encodedFolder = ClaudeMonitorProvider.EncodeProjectFolder(cwd);
        var projectDir = Path.Combine(fx.ClaudeHomePath, "projects", encodedFolder);
        Directory.CreateDirectory(projectDir);

        await File.WriteAllTextAsync(Path.Combine(sessionsDir, "9999.json"), $$"""
            {
              "pid": 9999,
              "sessionId": "{{sessionId}}",
              "cwd": "{{cwd.Replace("\\", "\\\\")}}",
              "name": "Claude Monitor",
              "status": "idle",
              "updatedAt": 1795036303390
            }
            """);

        const string secretPrompt = "PRIVATE_USER_PROMPT_SHOULD_NEVER_LEAK";
        var line1 = $$$"""{"type":"user","message":{"role":"user","content":[{"type":"text","text":"{{{secretPrompt}}}"}]}}""";
        const string line2 = """{"type":"assistant","message":{"role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"You've hit your monthly spend limit · your 5-hour limit resets 12am (Europe/Berlin)"}]}}""";
        await File.WriteAllLinesAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), [line1, line2]);

        var provider = new ClaudeMonitorProvider();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var monitor = await provider.GetMonitorAsync(paths, CancellationToken.None);

        Assert.Equal("rate_limited", monitor.State);
        Assert.Equal("claude-opus-5-5", monitor.Model);
        Assert.Equal("12am (Europe/Berlin)", monitor.RateLimitResetLabel);
        Assert.Equal(1, monitor.ActiveSessionCount);
        Assert.DoesNotContain(secretPrompt, monitor.Source.Notice ?? string.Empty);

        // Also verifies German monthly spend-limit wording is detected accurately
        const string germanLimitLine = """{"type":"assistant","message":{"role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"Monatliches Ausgabenlimit erreicht\nErhöhe dein Limit, um weiterzuarbeiten · Die Plannutzung wird am Mo., 23. Nov., 20:00 zurückgesetzt"}]}}""";
        await File.WriteAllLinesAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), [germanLimitLine]);
        var germanMonitor = await provider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("rate_limited", germanMonitor.State);
        Assert.Equal("Mo., 23. Nov., 20:00", germanMonitor.RateLimitResetLabel);

        // Also verifies rate_limited quota pause remains visible even after local session file is closed
        Directory.Delete(sessionsDir, recursive: true);
        const string closedWeeklyLimitLine = """{"type":"assistant","message":{"role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"You've hit your monthly spend limit · your weekly limit resets Nov 23, 8pm (Europe/Berlin)"}]}}""";
        await File.WriteAllLinesAsync(Path.Combine(projectDir, $"{sessionId}.jsonl"), [closedWeeklyLimitLine]);
        var closedMonitor = await provider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("rate_limited", closedMonitor.State);
        Assert.Equal("Nov 23, 8pm (Europe/Berlin)", closedMonitor.RateLimitResetLabel);
        Assert.Equal(0, closedMonitor.ActiveSessionCount);
    }

    [Fact]
    public async Task CodexMonitorProvider_ReadsStandingThreadIgnoresGuardianAndReadsAutomation()
    {
        using var fx = new TempDashboardFixture();
        var dbPath = Path.Combine(fx.CodexHomePath, "state_5.sqlite");
        var nowEpoch = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeSeconds();

        TempDashboardFixture.CreateSqliteDb(dbPath, $"""
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('t1', 'Codex Monitor', 'gpt-6.2-sol', 0, {nowEpoch});
            INSERT INTO threads VALUES ('t2', 'Guardian review', 'codex-auto-review', 0, {nowEpoch});
            INSERT INTO threads VALUES ('t3', 'Archived Old', 'gpt-6.1-sol', 1, {nowEpoch - 5000});
            """);

        var autoDir = Path.Combine(fx.CodexHomePath, "automations", "td-hb");
        Directory.CreateDirectory(autoDir);
        await File.WriteAllTextAsync(Path.Combine(autoDir, "automation.toml"), """
            version = 1
            status = "ACTIVE"
            rrule = "FREQ=HOURLY;INTERVAL=1"
            """);

        var provider = new CodexMonitorProvider();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var monitor = await provider.GetMonitorAsync(paths, CancellationToken.None);

        Assert.Equal("ok", monitor.Source.Status);
        Assert.Equal("standby", monitor.State);
        Assert.Equal("gpt-6.2-sol", monitor.Model);
        Assert.Equal(1, monitor.ActiveSessionCount);
        Assert.Equal("ACTIVE (FREQ=HOURLY;INTERVAL=1)", monitor.AutomationStatus);
    }

    [Fact]
    public async Task AntigravityMonitorProvider_MapsRunningAndDegradesGracefullyWhenMissing()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var provider = new AntigravityMonitorProvider();
        var missingResult = await provider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("offline", missingResult.State);
        Assert.Equal("unavailable", missingResult.Source.Status);

        var dbPath = Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db");
        TempDashboardFixture.CreateSqliteDb(dbPath, """
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('c1', 'Antigravity Monitor', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-19T11:40:00Z', '');
            INSERT INTO conversation_summaries VALUES ('c2', 'CLI Check', 'CASCADE_RUN_STATUS_IDLE', '2026-11-18T10:00:00Z', 'CORTEX_TRAJECTORY_SOURCE_CLI');
            """);

        var runningResult = await provider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("ok", runningResult.Source.Status);
        Assert.Equal("busy", runningResult.State);
        Assert.Equal("Orchestrator (Neon)", runningResult.Model);
        Assert.Equal(1, runningResult.ActiveSessionCount);
        Assert.Equal(0, runningResult.ActiveSubAgentCount);
    }

    [Fact]
    public async Task Providers_CountActiveRuntimeSubAgents_StrictlyReadOnly()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        // 1. Codex: standing monitor + 1 recent unnamed subagent + 1 recent Guardian review + 1 stale subagent
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.CodexHomePath, "state_5.sqlite"), $"""
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('t-mon', 'Codex Monitor', 'gpt-6.2-sol', 0, {nowEpoch - 600});
            INSERT INTO threads VALUES ('t-sub1', NULL, 'gpt-6.1-sol', 0, {nowEpoch - 20});
            INSERT INTO threads VALUES ('t-guard', 'Guardian review', 'codex-auto-review', 0, {nowEpoch - 30});
            INSERT INTO threads VALUES ('t-old', NULL, 'gpt-6.1-sol', 0, {nowEpoch - 3600});
            """);
        var codex = await new CodexMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal(1, codex.ActiveSessionCount);
        Assert.Equal(2, codex.ActiveSubAgentCount);

        // 2. Claude: standing monitor + 1 busy extra session + 1 recent subagent .jsonl (content unread) + 1 stale subagent .jsonl
        var sessionsDir = Path.Combine(fx.ClaudeHomePath, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var monitorSessionId = "22222222-2222-2222-2222-222222222222";
        var subagentsDir = Path.Combine(fx.ClaudeHomePath, "projects", "test-proj", monitorSessionId, "subagents");
        Directory.CreateDirectory(subagentsDir);
        await File.WriteAllTextAsync(Path.Combine(sessionsDir, "1001.json"), $$"""
            { "sessionId": "{{monitorSessionId}}", "name": "Claude Monitor", "status": "idle", "updatedAt": 1795036303390 }
            """);
        await File.WriteAllTextAsync(Path.Combine(sessionsDir, "1002.json"), """
            { "sessionId": "33333333-3333-3333-3333-333333333333", "name": "", "status": "busy", "updatedAt": 1795036303390 }
            """);
        var recentSubFile = Path.Combine(subagentsDir, "agent-active.jsonl");
        var staleSubFile = Path.Combine(subagentsDir, "agent-stale.jsonl");
        await File.WriteAllTextAsync(recentSubFile, """{"secret":"NEVER_READ"}""");
        await File.WriteAllTextAsync(staleSubFile, """{"secret":"NEVER_READ"}""");
        File.SetLastWriteTimeUtc(recentSubFile, DateTime.UtcNow.AddSeconds(-20));
        File.SetLastWriteTimeUtc(staleSubFile, DateTime.UtcNow.AddMinutes(-15));

        var claude = await new ClaudeMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal(2, claude.ActiveSessionCount);
        Assert.Equal(2, claude.ActiveSubAgentCount);

        // 3. Antigravity: standing monitor + 1 running subagent + 1 running CLI agent + 1 idle conversation
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db"), """
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('c1', 'Antigravity Monitor', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-21T20:00:00Z', '');
            INSERT INTO conversation_summaries VALUES ('c2', '', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-21T20:01:00Z', '');
            INSERT INTO conversation_summaries VALUES ('c3', 'Sub Worker', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-21T20:01:00Z', 'CORTEX_TRAJECTORY_SOURCE_CLI');
            INSERT INTO conversation_summaries VALUES ('c4', 'Old CLI', 'CASCADE_RUN_STATUS_IDLE', '2026-11-20T10:00:00Z', 'CORTEX_TRAJECTORY_SOURCE_CLI');
            """);
        var antigravity = await new AntigravityMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal(1, antigravity.ActiveSessionCount);
        Assert.Equal(2, antigravity.ActiveSubAgentCount);
    }
}
