using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
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
        var recentIso = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O");
        TempDashboardFixture.CreateSqliteDb(dbPath, $"""
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('c1', 'Antigravity Monitor', 'CASCADE_RUN_STATUS_RUNNING', '{recentIso}', '');
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

    [Fact]
    public async Task RootHeadProviders_DetectGptClaudeArgonAndOpusHeadsAndTranscriptModels()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var nowEpoch = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds();
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.CodexHomePath, "state_5.sqlite"), $"""
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('t1', 'GPT Head', 'gpt-6.2-sol', 0, {nowEpoch});
            """);

        var sessionsDir = Path.Combine(fx.ClaudeHomePath, "sessions");
        Directory.CreateDirectory(sessionsDir);
        await File.WriteAllTextAsync(Path.Combine(sessionsDir, "20548.json"), """
            { "sessionId": "44444444-4444-4444-4444-444444444444", "name": "Claude Head", "status": "idle", "updatedAt": 1795418444682 }
            """);

        var recentIso = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db"), $"""
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('argon-1', 'Argon Head', 'CASCADE_RUN_STATUS_RUNNING', '{recentIso}', '');
            INSERT INTO conversation_summaries VALUES ('opus-1', 'Opus Head', 'CASCADE_RUN_STATUS_IDLE', '2026-11-23T07:14:48Z', '');
            INSERT INTO conversation_summaries VALUES ('worker-1', 'Worker Slice', 'CASCADE_RUN_STATUS_RUNNING', '{recentIso}', 'CORTEX_TRAJECTORY_SOURCE_CLI');
            """);

        var argonLogDir = Path.Combine(fx.AntigravityHomePath, "brain", "argon-1", ".system_generated", "logs");
        var opusLogDir = Path.Combine(fx.AntigravityHomePath, "brain", "opus-1", ".system_generated", "logs");
        var workerLogDir = Path.Combine(fx.AntigravityHomePath, "brain", "worker-1", ".system_generated", "logs");
        Directory.CreateDirectory(argonLogDir);
        Directory.CreateDirectory(opusLogDir);
        Directory.CreateDirectory(workerLogDir);
        await File.WriteAllTextAsync(Path.Combine(argonLogDir, "transcript.jsonl"), "{\"model\":\"gemini-4-argon-medium\"}\n{\"model\":\"gemini-4-hydrogen-xhigh\"}\n");
        await File.WriteAllTextAsync(Path.Combine(opusLogDir, "transcript.jsonl"), "{\"model\":\"claude-opus-5-5-medium\"}\n{\"model\":\"gemini-4-hydrogen-xhigh\"}\n");
        await File.WriteAllTextAsync(Path.Combine(workerLogDir, "transcript.jsonl"), "{\"model\":\"gemini-4-hydrogen-xhigh\"}\n");

        var gpt = await new CodexMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        var claude = await new ClaudeMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        var antigravityHeads = await new AntigravityMonitorProvider().GetMonitorsAsync(paths, CancellationToken.None);

        Assert.Equal("MOBAi ChatGPT Codex", gpt.DisplayName);
        Assert.Equal("MOBAi Claude Opus", claude.DisplayName);
        Assert.Equal(2, antigravityHeads.Count);
        Assert.Equal("MOBAi Antigravity Gemini", antigravityHeads[0].DisplayName);
        Assert.Equal("Orchestrator · Gemini 4 Argon", antigravityHeads[0].Model);
        Assert.Equal("busy", antigravityHeads[0].State);
        Assert.Equal(1, antigravityHeads[0].ActiveSessionCount);
        Assert.Equal(1, antigravityHeads[0].ActiveSubAgentCount);
        Assert.NotNull(antigravityHeads[0].ActiveSubAgentsByModel);
        Assert.Equal(1, antigravityHeads[0].ActiveSubAgentsByModel!["Gemini 4 Hydrogen"]);
        Assert.Null(antigravityHeads[0].Source.Notice);

        Assert.Equal("MOBAi Antigravity Opus", antigravityHeads[1].DisplayName);
        Assert.Equal("Orchestrator · Claude Opus 5.5", antigravityHeads[1].Model);
        Assert.Equal("standby", antigravityHeads[1].State);
        Assert.Equal(1, antigravityHeads[1].ActiveSessionCount);
        Assert.Equal(0, antigravityHeads[1].ActiveSubAgentCount);

        var summary = MobaDev.Dashboard.Api.Contracts.SubAgentModelCatalog.BuildSummaryList([gpt, claude, .. antigravityHeads]);
        Assert.Contains(summary, x => x.Model == "Gemini 4 Hydrogen" && x.Profile == "P4" && x.Count == 1);
        Assert.Contains(summary, x => x.Model == "GPT-6.2-sol" && x.Profile == "P2" && x.Count == 0);
        Assert.Contains(summary, x => x.Model == "GPT-6.2-astra" && x.Profile == "P2" && x.Count == 0);
        Assert.Contains(summary, x => x.Model == "Claude Opus 5.5" && x.Profile == "P1" && x.Count == 0);
        Assert.Contains(summary, x => x.Model == "Claude Haiku 4.5" && x.Profile == "P4" && x.Count == 0);
        Assert.Equal(("GPT-6.2-astra", "P2"), MobaDev.Dashboard.Api.Contracts.SubAgentModelCatalog.Normalize("gpt-6.2-astra"));
        Assert.Equal(("Claude Haiku 4.5", "P4"), MobaDev.Dashboard.Api.Contracts.SubAgentModelCatalog.Normalize("claude-haiku-4-5-20251001"));
        Assert.Equal(8, summary.Count);
    }

    [Fact]
    public async Task CodexMonitorProvider_DetectsUsageLimitExceededAndClearsAfterSuccessfulTurn()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var recentActivityEpoch = DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds();
        var standbyActivityEpoch = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var futureResetEpoch = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();

        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.CodexHomePath, "state_5.sqlite"), $"""
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('t1', 'GPT Head', 'gpt-6.2-sol', 0, {recentActivityEpoch});
            """);

        var rolloutDir = Path.Combine(fx.CodexHomePath, "sessions", "2026", "11", "23");
        Directory.CreateDirectory(rolloutDir);
        var rolloutPath = Path.Combine(rolloutDir, "rollout-2026-11-23T17-00-00-t1.jsonl");

        const string secretPrompt = "PRIVATE_CODEX_PROMPT_SHOULD_NEVER_LEAK";
        var promptLine = $$$"""{"timestamp":"2026-11-23T17:02:00Z","type":"event_msg","payload":{"type":"user_message","message":"{{{secretPrompt}}}"}}""";
        var tokenLine = """{"timestamp":"2026-11-23T17:02:39Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":99.0,"window_minutes":300,"resets_at":__RESET__},"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"rate_limit_reached_type":null}}}"""
            .Replace("__RESET__", futureResetEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var premiumNullLine = """{"timestamp":"2026-11-23T17:02:41Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null,"rate_limit_reached_type":null}}}""";
        const string limitCompleteLine = """{"timestamp":"2026-11-23T17:02:42Z","type":"event_msg","payload":{"type":"task_complete","error":{"message":"You’ve hit your usage limit. Upgrade to Pro (https://chatgpt.com/explore/pro), visit https://chatgpt.com/settings/usage to purchase more credits or try again at 8:46 PM.","codex_error_info":"usage_limit_exceeded"}}}""";

        await File.WriteAllLinesAsync(rolloutPath, [promptLine, tokenLine, premiumNullLine, limitCompleteLine]);

        var provider = new CodexMonitorProvider();
        var limitedMonitor = await provider.GetMonitorAsync(paths, CancellationToken.None);

        Assert.Equal("rate_limited", limitedMonitor.State);
        Assert.Equal("8:46 PM · usage limit exceeded", limitedMonitor.RateLimitResetLabel);
        Assert.DoesNotContain(secretPrompt, limitedMonitor.Source.Notice ?? string.Empty);

        // Move thread activity past the busy window and append a subsequent successful turn.
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.CodexHomePath, "state_5.sqlite"), $"""
            DROP TABLE IF EXISTS threads;
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('t1', 'GPT Head', 'gpt-6.2-sol', 0, {standbyActivityEpoch});
            """);
        const string recoveredTurnLine = """{"timestamp":"2026-11-23T19:50:00Z","type":"event_msg","payload":{"type":"task_complete","last_agent_message":"Resumed successfully","error":null}}""";
        await File.AppendAllLinesAsync(rolloutPath, [recoveredTurnLine]);

        var recoveredMonitor = await provider.GetMonitorAsync(paths, CancellationToken.None);

        Assert.Equal("standby", recoveredMonitor.State);
        Assert.Null(recoveredMonitor.RateLimitResetLabel);

        // Also verifies 100% rate_limits saturation in a newer session triggers rate_limited.
        var saturatedTokenLine = """{"timestamp":"2026-11-23T20:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":100.0,"window_minutes":300,"resets_at":__RESET__},"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"rate_limit_reached_type":"primary"}}}"""
            .Replace("__RESET__", futureResetEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await File.AppendAllLinesAsync(rolloutPath, [saturatedTokenLine]);

        var saturatedMonitor = await provider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("rate_limited", saturatedMonitor.State);
        Assert.NotNull(saturatedMonitor.RateLimitResetLabel);
    }

    [Fact]
    public async Task Providers_FilterByMobaiPrefixAndIgnoreForeignProjectSessions()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var nowEpoch = DateTimeOffset.UtcNow.AddSeconds(-20).ToUnixTimeSeconds();
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.CodexHomePath, "state_5.sqlite"), $"""
            CREATE TABLE threads (id TEXT PRIMARY KEY, name TEXT, model TEXT, archived INTEGER, updated_at INTEGER);
            INSERT INTO threads VALUES ('moba-gpt', 'MOBAi ChatGPT Codex', 'gpt-6.2-sol', 0, {nowEpoch});
            INSERT INTO threads VALUES ('other-gpt', 'NEXTi ChatGPT Codex', 'gpt-6.2-sol', 0, {nowEpoch});
            """);

        var claudeSessionsDir = Path.Combine(fx.ClaudeHomePath, "sessions");
        Directory.CreateDirectory(claudeSessionsDir);
        await File.WriteAllTextAsync(Path.Combine(claudeSessionsDir, "3001.json"), """
            { "sessionId": "55555555-5555-5555-5555-555555555555", "name": "MOBAi Claude Opus", "status": "idle", "updatedAt": 1795418444682 }
            """);
        await File.WriteAllTextAsync(Path.Combine(claudeSessionsDir, "3002.json"), """
            { "sessionId": "66666666-6666-6666-6666-666666666666", "name": "NEXTi Claude Opus", "status": "busy", "updatedAt": 1795418444999 }
            """);
        await File.WriteAllTextAsync(Path.Combine(claudeSessionsDir, "3003.json"), """
            { "sessionId": "77777777-7777-7777-7777-777777777777", "name": "", "cwd": "C:\\OtherRepo\\App", "status": "busy", "updatedAt": 1795418444999 }
            """);

        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db"), """
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('moba-gem', 'MOBAi Antigravity Gemini', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-24T13:20:00Z', '');
            INSERT INTO conversation_summaries VALUES ('moba-opus', 'MOBAi Antigravity Opus', 'CASCADE_RUN_STATUS_IDLE', '2026-11-24T13:19:00Z', '');
            INSERT INTO conversation_summaries VALUES ('other-gem', 'NEXTi Antigravity Gemini', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-24T13:21:00Z', '');
            INSERT INTO conversation_summaries VALUES ('random-chat', 'Random Chat', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-24T13:21:30Z', '');
            """);

        var gemLogsDir = Path.Combine(fx.AntigravityHomePath, "brain", "moba-gem", ".system_generated", "logs");
        Directory.CreateDirectory(gemLogsDir);
        await File.WriteAllTextAsync(Path.Combine(gemLogsDir, "transcript.jsonl"), "{\"model\":\"gemini-4-carbon-medium\"}\n");

        var codex = await new CodexMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        var claude = await new ClaudeMonitorProvider().GetMonitorAsync(paths, CancellationToken.None);
        var antigravityHeads = await new AntigravityMonitorProvider().GetMonitorsAsync(paths, CancellationToken.None);

        Assert.Equal("MOBAi ChatGPT Codex", codex.DisplayName);
        Assert.Equal(1, codex.ActiveSessionCount);
        Assert.Equal(0, codex.ActiveSubAgentCount);
        Assert.Null(codex.Source.Notice);

        Assert.Equal("MOBAi Claude Opus", claude.DisplayName);
        Assert.Equal(1, claude.ActiveSessionCount);
        Assert.Equal(0, claude.ActiveSubAgentCount);
        Assert.Null(claude.Source.Notice);

        Assert.Equal(2, antigravityHeads.Count);
        Assert.Equal("MOBAi Antigravity Gemini", antigravityHeads[0].DisplayName);
        Assert.Equal("Orchestrator · Gemini 4 Carbon", antigravityHeads[0].Model);
        Assert.Equal(1, antigravityHeads[0].ActiveSessionCount);
        Assert.Equal(0, antigravityHeads[0].ActiveSubAgentCount);
        Assert.Null(antigravityHeads[0].Source.Notice);

        Assert.Equal("MOBAi Antigravity Opus", antigravityHeads[1].DisplayName);
        Assert.Equal(1, antigravityHeads[1].ActiveSessionCount);
        Assert.Equal(0, antigravityHeads[1].ActiveSubAgentCount);
        Assert.Null(antigravityHeads[1].Source.Notice);
    }

    [Fact]
    public async Task AntigravityMonitorProvider_AttributesSubAgentsByParentTranscriptAndRecognizesGptOss120B()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db"), """
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('gem-root', 'MOBAi Antigravity Gemini', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-24T16:00:00Z', '');
            INSERT INTO conversation_summaries VALUES ('opus-root', 'MOBAi Antigravity Opus', 'CASCADE_RUN_STATUS_IDLE', '2026-11-24T15:59:00Z', '');
            INSERT INTO conversation_summaries VALUES ('sub-opus', 'Sub Opus Worker', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-24T16:01:00Z', 'CORTEX_TRAJECTORY_SOURCE_CLI');
            INSERT INTO conversation_summaries VALUES ('sub-oss', 'Sub Oss Verifier', 'CASCADE_RUN_STATUS_RUNNING', '2026-11-24T16:01:05Z', 'CORTEX_TRAJECTORY_SOURCE_CLI');
            """);

        var gemLogs = Path.Combine(fx.AntigravityHomePath, "brain", "gem-root", ".system_generated", "logs");
        var opusLogs = Path.Combine(fx.AntigravityHomePath, "brain", "opus-root", ".system_generated", "logs");
        var subOpusLogs = Path.Combine(fx.AntigravityHomePath, "brain", "sub-opus", ".system_generated", "logs");
        var subOssLogs = Path.Combine(fx.AntigravityHomePath, "brain", "sub-oss", ".system_generated", "logs");
        Directory.CreateDirectory(gemLogs);
        Directory.CreateDirectory(opusLogs);
        Directory.CreateDirectory(subOpusLogs);
        Directory.CreateDirectory(subOssLogs);

        await File.WriteAllTextAsync(
            Path.Combine(gemLogs, "transcript.jsonl"),
            "{\"model\":\"gemini-4-carbon-medium\"}\n" +
            "{\"tool\":\"invoke_subagent\",\"content\":\"{\\\"conversationId\\\": \\\"sub-opus\\\"}\"}\n" +
            "{\"tool\":\"invoke_subagent\",\"content\":\"{\\\"conversationId\\\": \\\"sub-oss\\\"}\"}\n");
        await File.WriteAllTextAsync(Path.Combine(opusLogs, "transcript.jsonl"), "{\"model\":\"claude-opus-5-5-medium\"}\n");
        await File.WriteAllTextAsync(Path.Combine(subOpusLogs, "transcript.jsonl"), "{\"model\":\"claude-opus-5-5-medium\"}\n");
        await File.WriteAllTextAsync(Path.Combine(subOssLogs, "transcript.jsonl"), "{\"model\":\"gpt-oss-120b\"}\n");

        var heads = await new AntigravityMonitorProvider().GetMonitorsAsync(paths, CancellationToken.None);

        Assert.Equal(2, heads.Count);
        Assert.Equal("MOBAi Antigravity Gemini", heads[0].DisplayName);
        Assert.Equal("Orchestrator · Gemini 4 Carbon", heads[0].Model);
        Assert.Equal(2, heads[0].ActiveSubAgentCount);
        Assert.NotNull(heads[0].ActiveSubAgentsByModel);
        Assert.Equal(1, heads[0].ActiveSubAgentsByModel!["Claude Opus 5.5"]);
        Assert.Equal(1, heads[0].ActiveSubAgentsByModel!["GPT-OSS 120B"]);

        Assert.Equal("MOBAi Antigravity Opus", heads[1].DisplayName);
        Assert.Equal(0, heads[1].ActiveSubAgentCount);
    }

    [Fact]
    public async Task AntigravityMonitorProvider_DetectsPartnerQuotaExhaustionFromTranscriptAndConversationDb()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var nowIso = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O");
        var futureResetUtc = DateTimeOffset.UtcNow.AddDays(3);
        var futureResetIso = futureResetUtc.ToString("O");
        var expectedLocal = futureResetUtc.ToLocalTime();
        var expectedResetLabel = expectedLocal.Date == DateTimeOffset.UtcNow.ToLocalTime().Date
            ? $"{expectedLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} · quota exhausted"
            : $"{expectedLocal.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture)} · quota exhausted";

        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db"), $"""
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('gem-root', 'MOBAi Antigravity Gemini', 'CASCADE_RUN_STATUS_RUNNING', '{nowIso}', '');
            INSERT INTO conversation_summaries VALUES ('opus-root', 'MOBAi Antigravity Opus', 'CASCADE_RUN_STATUS_IDLE', '{nowIso}', '');
            """);

        var gemLogs = Path.Combine(fx.AntigravityHomePath, "brain", "gem-root", ".system_generated", "logs");
        var opusLogs = Path.Combine(fx.AntigravityHomePath, "brain", "opus-root", ".system_generated", "logs");
        Directory.CreateDirectory(gemLogs);
        Directory.CreateDirectory(opusLogs);

        await File.WriteAllTextAsync(Path.Combine(gemLogs, "transcript.jsonl"), "{\"model\":\"gemini-4-carbon-medium\"}\n");
        await File.WriteAllTextAsync(Path.Combine(opusLogs, "transcript.jsonl"), "{\"model\":\"claude-opus-5-5-medium\"}\n");

        // Signal 1: escaped swarm models quota JSON in transcript_full.jsonl
        await File.WriteAllTextAsync(
            Path.Combine(gemLogs, "transcript_full.jsonl"),
            "{\"step\":1,\"content\":\"{\\\"claude-opus-5-5-medium\\\":{\\\"quota_remaining\\\":\\\"0%\\\",\\\"quota_resets\\\":\\\"" +
            futureResetIso +
            "\\\"},\\\"gemini-4-carbon-medium\\\":{\\\"quota_remaining\\\":\\\"93%\\\",\\\"quota_resets\\\":\\\"" +
            futureResetIso +
            "\\\"}}\"}\n");

        var provider = new AntigravityMonitorProvider();
        var signal1Heads = await provider.GetMonitorsAsync(paths, CancellationToken.None);

        Assert.Equal(2, signal1Heads.Count);
        Assert.Equal("busy", signal1Heads[0].State);
        Assert.Null(signal1Heads[0].RateLimitResetLabel);
        Assert.Equal("rate_limited", signal1Heads[1].State);
        Assert.Equal(expectedResetLabel, signal1Heads[1].RateLimitResetLabel);

        // Signal 2 fallback: transcript_full.jsonl has no quota_remaining snapshot; conversations/<opusId>.db has HTTP 429 BLOB
        File.Delete(Path.Combine(gemLogs, "transcript_full.jsonl"));
        const string secretUserText = "TOP_SECRET_USER_TEXT_MUST_NEVER_LEAK";
        var convDbPath = Path.Combine(fx.AntigravityHomePath, "conversations", "opus-root.db");
        TempDashboardFixture.CreateSqliteDb(convDbPath, $"""
            CREATE TABLE steps (
                idx INTEGER PRIMARY KEY,
                step_type INTEGER,
                step_payload BLOB
            );
            INSERT INTO steps VALUES (0, 14, '{secretUserText}');
            INSERT INTO steps VALUES (1, 15, X'7B7D');
            INSERT INTO steps VALUES (2, 17, CAST('RESOURCE_EXHAUSTED (code 429) INSUFFICIENT_G1_CREDITS_BALANCE' AS BLOB));
            """);

        var signal2Heads = await provider.GetMonitorsAsync(paths, CancellationToken.None);

        Assert.Equal("rate_limited", signal2Heads[1].State);
        Assert.Equal("Quota exhausted (HTTP 429)", signal2Heads[1].RateLimitResetLabel);
        Assert.DoesNotContain(secretUserText, signal2Heads[1].Source.Notice ?? string.Empty);
    }

    [Fact]
    public async Task Providers_DowngradeStaleBusyMonitorsToIdleAndHonorConfigurableThreshold()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var now = DateTimeOffset.UtcNow;
        var stale30MinAgo = now.AddMinutes(-30);
        var active2MinAgo = now.AddMinutes(-2);

        // (a) Antigravity running + last activity 30 min ago -> "idle"
        // (b) Antigravity running + last activity 2 min ago -> "busy"
        var agDbPath = Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db");
        TempDashboardFixture.CreateSqliteDb(agDbPath, $"""
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('gem-stale', 'MOBAi Antigravity Gemini', 'CASCADE_RUN_STATUS_RUNNING', '{stale30MinAgo:O}', '');
            INSERT INTO conversation_summaries VALUES ('opus-active', 'MOBAi Antigravity Opus', 'CASCADE_RUN_STATUS_RUNNING', '{active2MinAgo:O}', '');
            """);

        var agProvider = new AntigravityMonitorProvider();
        var agMonitors = await agProvider.GetMonitorsAsync(paths, CancellationToken.None);

        Assert.Equal(2, agMonitors.Count);
        Assert.Equal("idle", agMonitors[0].State);
        Assert.Equal("busy", agMonitors[1].State);

        // (c) Claude busy + stale activity -> "idle"
        var claudeSessionsDir = Path.Combine(fx.ClaudeHomePath, "sessions");
        Directory.CreateDirectory(claudeSessionsDir);
        var claudeSessionFile = Path.Combine(claudeSessionsDir, "9001.json");
        await File.WriteAllTextAsync(claudeSessionFile, $$"""
            {
              "sessionId": "88888888-8888-8888-8888-888888888888",
              "name": "MOBAi Claude Opus",
              "status": "busy",
              "updatedAt": {{stale30MinAgo.ToUnixTimeMilliseconds()}}
            }
            """);

        var claudeProvider = new ClaudeMonitorProvider();
        var claudeIdle = await claudeProvider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("idle", claudeIdle.State);

        // (d) Configurable BusyIdleAfterMinutes threshold:
        // - 45 min keeps a 30-min-old session "busy"
        // - 1 min downgrades a 2-min-old session to "idle"
        // - Non-positive BusyIdleAfterMinutes falls back to default 10 min
        var lenientOptions = new DashboardOptions { BusyIdleAfterMinutes = 45 };
        var strictOptions = new DashboardOptions { BusyIdleAfterMinutes = 1 };
        var invalidOptions = new DashboardOptions { BusyIdleAfterMinutes = 0 };

        Assert.Equal(TimeSpan.FromMinutes(10), invalidOptions.ResolveBusyIdleThreshold());

        var agLenient = await agProvider.GetMonitorsAsync(paths, lenientOptions.ResolveBusyIdleThreshold(), CancellationToken.None);
        Assert.Equal("busy", agLenient[0].State);
        Assert.Equal("busy", agLenient[1].State);

        var agStrict = await agProvider.GetMonitorsAsync(paths, strictOptions.ResolveBusyIdleThreshold(), CancellationToken.None);
        Assert.Equal("idle", agStrict[0].State);
        Assert.Equal("idle", agStrict[1].State);

        var claudeLenient = await claudeProvider.GetMonitorAsync(paths, lenientOptions.ResolveBusyIdleThreshold(), CancellationToken.None);
        Assert.Equal("busy", claudeLenient.State);

        // MonitorStateRules.ApplyIdle direct checks
        Assert.Equal("idle", MonitorStateRules.ApplyIdle("BUSY", null, now, TimeSpan.FromMinutes(10)));
        Assert.Equal("busy", MonitorStateRules.ApplyIdle("busy", now.AddMinutes(-10), now, TimeSpan.FromMinutes(10)));
        Assert.Equal("idle", MonitorStateRules.ApplyIdle("busy", now.AddMinutes(-10).AddSeconds(-1), now, TimeSpan.FromMinutes(10)));
        Assert.Equal("standby", MonitorStateRules.ApplyIdle("standby", stale30MinAgo, now, TimeSpan.FromMinutes(10)));
        Assert.Equal("rate_limited", MonitorStateRules.ApplyIdle("rate_limited", stale30MinAgo, now, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task Providers_StaleBusyWithActiveQuotaPausePreservesRateLimitedStateAndResetLabel()
    {
        using var fx = new TempDashboardFixture();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var now = DateTimeOffset.UtcNow;
        var stale30MinAgo = now.AddMinutes(-30);
        var active2MinAgo = now.AddMinutes(-2);
        var futureResetUtc = now.AddDays(2);
        var futureResetIso = futureResetUtc.ToString("O");
        var expectedLocal = futureResetUtc.ToLocalTime();
        var expectedAgResetLabel = expectedLocal.Date == now.ToLocalTime().Date
            ? $"{expectedLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} · quota exhausted"
            : $"{expectedLocal.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture)} · quota exhausted";

        // 1. Antigravity: stale RUNNING (30 min ago) + active quota pause -> "rate_limited" with non-null ResetLabel
        //    while active RUNNING (2 min ago) + quota snapshot stays "busy".
        TempDashboardFixture.CreateSqliteDb(Path.Combine(fx.AntigravityHomePath, "conversation_summaries.db"), $"""
            CREATE TABLE conversation_summaries (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                status TEXT,
                last_modified_time TEXT,
                source TEXT
            );
            INSERT INTO conversation_summaries VALUES ('gem-stale-limit', 'MOBAi Antigravity Gemini', 'CASCADE_RUN_STATUS_RUNNING', '{stale30MinAgo:O}', '');
            INSERT INTO conversation_summaries VALUES ('opus-active-running', 'MOBAi Antigravity Opus', 'CASCADE_RUN_STATUS_RUNNING', '{active2MinAgo:O}', '');
            """);

        var gemLogs = Path.Combine(fx.AntigravityHomePath, "brain", "gem-stale-limit", ".system_generated", "logs");
        var opusLogs = Path.Combine(fx.AntigravityHomePath, "brain", "opus-active-running", ".system_generated", "logs");
        Directory.CreateDirectory(gemLogs);
        Directory.CreateDirectory(opusLogs);

        await File.WriteAllTextAsync(Path.Combine(gemLogs, "transcript.jsonl"), "{\"model\":\"gemini-4-carbon-medium\"}\n");
        await File.WriteAllTextAsync(Path.Combine(opusLogs, "transcript.jsonl"), "{\"model\":\"claude-opus-5-5-medium\"}\n");
        await File.WriteAllTextAsync(
            Path.Combine(gemLogs, "transcript_full.jsonl"),
            "{\"step\":1,\"content\":\"{\\\"gemini-4-carbon-medium\\\":{\\\"quota_remaining\\\":\\\"0%\\\",\\\"quota_resets\\\":\\\"" +
            futureResetIso +
            "\\\"},\\\"claude-opus-5-5-medium\\\":{\\\"quota_remaining\\\":\\\"0%\\\",\\\"quota_resets\\\":\\\"" +
            futureResetIso +
            "\\\"}}\"}\n");

        var agHeads = await new AntigravityMonitorProvider().GetMonitorsAsync(paths, CancellationToken.None);
        Assert.Equal(2, agHeads.Count);
        Assert.Equal("rate_limited", agHeads[0].State);
        Assert.Equal(expectedAgResetLabel, agHeads[0].RateLimitResetLabel);
        Assert.Equal("busy", agHeads[1].State);
        Assert.Null(agHeads[1].RateLimitResetLabel);

        // 2. Claude: stale busy (30 min ago) + active quota pause in transcript -> "rate_limited" with ResetLabel
        var claudeSessionsDir = Path.Combine(fx.ClaudeHomePath, "sessions");
        Directory.CreateDirectory(claudeSessionsDir);
        var claudeSessionId = "99999999-9999-9999-9999-999999999999";
        var cwd = Path.Combine(fx.MobaFlowPath, "worktree-stale-busy-limit");
        var encodedFolder = ClaudeMonitorProvider.EncodeProjectFolder(cwd);
        var projectDir = Path.Combine(fx.ClaudeHomePath, "projects", encodedFolder);
        Directory.CreateDirectory(projectDir);

        var sessionFile = Path.Combine(claudeSessionsDir, "9002.json");
        await File.WriteAllTextAsync(sessionFile, $$"""
            {
              "sessionId": "{{claudeSessionId}}",
              "cwd": "{{cwd.Replace("\\", "\\\\")}}",
              "name": "MOBAi Claude Opus",
              "status": "busy",
              "updatedAt": {{stale30MinAgo.ToUnixTimeMilliseconds()}}
            }
            """);
        await File.WriteAllLinesAsync(
            Path.Combine(projectDir, $"{claudeSessionId}.jsonl"),
            ["""{"type":"assistant","message":{"role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"You've hit your monthly spend limit · your 5-hour limit resets 12am (Europe/Berlin)"}]}}"""]);

        var claudeProvider = new ClaudeMonitorProvider();
        var claudeLimited = await claudeProvider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("rate_limited", claudeLimited.State);
        Assert.Equal("12am (Europe/Berlin)", claudeLimited.RateLimitResetLabel);

        // Active within threshold (2 min ago) remains "busy"
        await File.WriteAllTextAsync(sessionFile, $$"""
            {
              "sessionId": "{{claudeSessionId}}",
              "cwd": "{{cwd.Replace("\\", "\\\\")}}",
              "name": "MOBAi Claude Opus",
              "status": "busy",
              "updatedAt": {{active2MinAgo.ToUnixTimeMilliseconds()}}
            }
            """);
        var claudeActive = await claudeProvider.GetMonitorAsync(paths, CancellationToken.None);
        Assert.Equal("busy", claudeActive.State);
        Assert.Null(claudeActive.RateLimitResetLabel);
    }
}
