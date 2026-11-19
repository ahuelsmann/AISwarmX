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
    }

    [Fact]
    public async Task CodexMonitorProvider_ReadsStandingThreadIgnoresGuardianAndReadsAutomation()
    {
        using var fx = new TempDashboardFixture();
        var dbPath = Path.Combine(fx.CodexHomePath, "state_5.sqlite");
        var nowEpoch = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeSeconds();

        TempDashboardFixture.CreateSqliteDb(dbPath, $"""
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('t1', 'Codex Monitor', 'gpt-6.1-sol', 0, {nowEpoch});
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
        Assert.Equal("gpt-6.1-sol", monitor.Model);
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
        Assert.Equal(1, runningResult.ActiveSessionCount);
    }
}
