using System.Globalization;
using Microsoft.Data.Sqlite;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads standing Antigravity Monitor metadata read-only from ~/.gemini/antigravity/conversation_summaries.db.
/// </summary>
public sealed class AntigravityMonitorProvider
{
    private const string MonitorId = "antigravity-monitor";
    private const string DisplayName = "Antigravity Monitor";
    private const string Role = "Head of AI Strategy";
    private const string Profile = "P3";
    private const string Tool = "Antigravity";
    private const string DefaultDesktopModel = "Orchestrator (Neon)";

    public Task<MonitorInfo> GetMonitorAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        var dbPath = Path.Combine(paths.AntigravityHomePath, "conversation_summaries.db");
        if (!File.Exists(dbPath))
        {
            return Task.FromResult(Unavailable(now, "unavailable", "Antigravity database (conversation_summaries.db) not found."));
        }

        try
        {
            using var connection = SqliteReadOnlyHelper.OpenReadOnly(dbPath);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT conversation_id, title, status, last_modified_time, source
                FROM conversation_summaries
                ORDER BY last_modified_time DESC
                """;

            var runningConversationIds = new List<string>();
            var namedDesktopConversations = new List<AntigravityConversationRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var status = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var lastModifiedRaw = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var source = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

                if (string.Equals(status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(id))
                {
                    runningConversationIds.Add(id);
                }

                if (string.Equals(source, "CORTEX_TRAJECTORY_SOURCE_CLI", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                namedDesktopConversations.Add(new AntigravityConversationRecord(id, title, status, lastModifiedRaw));
            }

            if (namedDesktopConversations.Count == 0)
            {
                return Task.FromResult(Unavailable(
                    now,
                    "ok",
                    "No named Antigravity desktop session found.",
                    runningConversationIds.Count));
            }

            var selected = namedDesktopConversations
                .OrderByDescending(c => string.Equals(c.Title, DisplayName, StringComparison.OrdinalIgnoreCase))
                .First();

            DateTimeOffset? lastActivityUtc = DateTimeOffset.TryParse(
                selected.LastModifiedRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedTime)
                ? parsedTime
                : null;

            var state = string.Equals(selected.Status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase)
                ? "busy"
                : "standby";

            var activeSubAgentCount = runningConversationIds.Count(id =>
                !string.Equals(id, selected.Id, StringComparison.OrdinalIgnoreCase));

            var notice = namedDesktopConversations.Count > 1
                ? $"{namedDesktopConversations.Count} named Antigravity sessions found in summaries (target: 1)."
                : null;

            return Task.FromResult(new MonitorInfo(
                Id: MonitorId,
                DisplayName: DisplayName,
                Role: Role,
                Profile: Profile,
                Tool: Tool,
                State: state,
                Model: DefaultDesktopModel,
                LastActivityUtc: lastActivityUtc,
                RateLimitResetLabel: null,
                ActiveSessionCount: namedDesktopConversations.Count,
                AutomationStatus: null,
                Source: new SourceHealth("ok", now, notice),
                ActiveSubAgentCount: activeSubAgentCount));
        }
        catch (SqliteException ex) when (SqliteReadOnlyHelper.IsBusyOrLocked(ex))
        {
            return Task.FromResult(Unavailable(
                now,
                "degraded",
                "Database temporarily locked — retrying on next poll."));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Unavailable(
                now,
                "unavailable",
                $"Unable to read Antigravity status: {ex.Message}"));
        }
    }

    private static MonitorInfo Unavailable(
        DateTimeOffset now,
        string sourceStatus,
        string notice,
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
            AutomationStatus: null,
            Source: new SourceHealth(sourceStatus, now, notice),
            ActiveSubAgentCount: activeSubAgentCount);
    }

    private sealed record AntigravityConversationRecord(
        string Id,
        string Title,
        string Status,
        string LastModifiedRaw);
}
