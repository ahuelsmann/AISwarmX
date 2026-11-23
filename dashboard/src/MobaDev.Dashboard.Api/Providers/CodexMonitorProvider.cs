using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads standing GPT Head (formerly Codex Monitor) metadata read-only from ~/.codex/state_5.sqlite and automation.toml.
/// </summary>
public sealed partial class CodexMonitorProvider
{
    private const string MonitorId = "codex-monitor";
    private const string DisplayName = "GPT Head";
    private const string LegacyDisplayName = "Codex Monitor";
    private const string Role = "Technical Director & CPM";
    private const string Profile = "P2";
    private const string Tool = "ChatGPT Codex";
    private static readonly TimeSpan BusyThreshold = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SubAgentActiveWindow = TimeSpan.FromMinutes(3);

    public Task<MonitorInfo> GetMonitorAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        var dbPath = Path.Combine(paths.CodexHomePath, "state_5.sqlite");
        var automationStatus = ReadAutomationStatus(paths.CodexHomePath);

        if (!File.Exists(dbPath))
        {
            return Task.FromResult(Unavailable(now, "unavailable", "Codex database (state_5.sqlite) not found.", automationStatus));
        }

        try
        {
            using var connection = SqliteReadOnlyHelper.OpenReadOnly(dbPath);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, name, model, updated_at
                FROM threads
                WHERE archived = 0
                ORDER BY updated_at DESC
                """;

            var allActiveThreads = new List<CodexThreadRecord>();
            var standingThreads = new List<CodexThreadRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                var model = reader.IsDBNull(2) ? null : reader.GetString(2);
                var updatedAtSeconds = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);
                var isStanding = !string.IsNullOrWhiteSpace(name) &&
                                 !string.Equals(name, "Guardian review", StringComparison.OrdinalIgnoreCase);

                var record = new CodexThreadRecord(id, name ?? string.Empty, model, updatedAtSeconds);
                allActiveThreads.Add(record);
                if (isStanding)
                {
                    standingThreads.Add(record);
                }
            }

            var subAgentCutoffSeconds = now.Subtract(SubAgentActiveWindow).ToUnixTimeSeconds();

            if (standingThreads.Count == 0)
            {
                var fallbackSubAgents = allActiveThreads.Count(t => t.UpdatedAtSeconds >= subAgentCutoffSeconds);
                return Task.FromResult(Unavailable(now, "ok", "No active GPT Head thread found.", automationStatus, fallbackSubAgents));
            }

            var selected = standingThreads
                .OrderByDescending(t =>
                    string.Equals(t.Name, DisplayName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name, LegacyDisplayName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(t => t.UpdatedAtSeconds)
                .First();

            var lastActivityUtc = selected.UpdatedAtSeconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(selected.UpdatedAtSeconds)
                : (DateTimeOffset?)null;

            var state = lastActivityUtc.HasValue && (now - lastActivityUtc.Value) <= BusyThreshold
                ? "busy"
                : "standby";

            var activeSubAgentCount = allActiveThreads.Count(t =>
                !string.Equals(t.Id, selected.Id, StringComparison.Ordinal) &&
                t.UpdatedAtSeconds >= subAgentCutoffSeconds);

            var notice = standingThreads.Count > 1
                ? $"{standingThreads.Count} non-archived named Codex threads active (target: 1)."
                : null;

            return Task.FromResult(new MonitorInfo(
                Id: MonitorId,
                DisplayName: DisplayName,
                Role: Role,
                Profile: Profile,
                Tool: Tool,
                State: state,
                Model: selected.Model,
                LastActivityUtc: lastActivityUtc,
                RateLimitResetLabel: null,
                ActiveSessionCount: standingThreads.Count,
                AutomationStatus: automationStatus,
                Source: new SourceHealth("ok", now, notice),
                ActiveSubAgentCount: activeSubAgentCount));
        }
        catch (SqliteException ex) when (SqliteReadOnlyHelper.IsBusyOrLocked(ex))
        {
            return Task.FromResult(Unavailable(
                now,
                "degraded",
                "Database momentarily locked — retrying on next refresh.",
                automationStatus));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Unavailable(
                now,
                "unavailable",
                $"Codex state unreadable: {ex.Message}",
                automationStatus));
        }
    }

    private static MonitorInfo Unavailable(
        DateTimeOffset now,
        string sourceStatus,
        string notice,
        string? automationStatus,
        int activeSubAgentCount = 0)
    {
        return new MonitorInfo(
            Id: MonitorId,
            DisplayName: DisplayName,
            Role: Role,
            Profile: Profile,
            Tool: Tool,
            State: "offline",
            Model: null,
            LastActivityUtc: null,
            RateLimitResetLabel: null,
            ActiveSessionCount: 0,
            AutomationStatus: automationStatus,
            Source: new SourceHealth(sourceStatus, now, notice),
            ActiveSubAgentCount: activeSubAgentCount);
    }

    private static string? ReadAutomationStatus(string codexHomePath)
    {
        var automationsDir = Path.Combine(codexHomePath, "automations");
        if (!Directory.Exists(automationsDir))
        {
            return null;
        }

        foreach (var tomlPath in Directory.EnumerateFiles(automationsDir, "automation.toml", SearchOption.AllDirectories))
        {
            if (!PathWhitelistGuard.IsAllowedFilePath(tomlPath))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(tomlPath);
                var statusMatch = TomlStatusRegex().Match(content);
                if (!statusMatch.Success)
                {
                    continue;
                }

                var status = statusMatch.Groups[1].Value;
                var rruleMatch = TomlRruleRegex().Match(content);
                return rruleMatch.Success
                    ? $"{status} ({rruleMatch.Groups[1].Value})"
                    : status;
            }
            catch
            {
                // Ignore unreadable automation metadata.
            }
        }

        return null;
    }

    [GeneratedRegex("^status\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex TomlStatusRegex();

    [GeneratedRegex("^rrule\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex TomlRruleRegex();

    private sealed record CodexThreadRecord(string Id, string Name, string? Model, long UpdatedAtSeconds);
}
