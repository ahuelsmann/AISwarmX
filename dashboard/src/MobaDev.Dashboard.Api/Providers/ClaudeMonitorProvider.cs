using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads standing Claude Monitor metadata from ~/.claude/sessions/*.json and only model/rate-limit metadata
/// from the tail of the active session transcript without ever exposing chat text.
/// </summary>
public sealed partial class ClaudeMonitorProvider
{
    private const int TailReadBytes = 64 * 1024;
    private static readonly TimeSpan SubAgentActiveWindow = TimeSpan.FromMinutes(2);
    private const string MonitorId = "claude-monitor";
    private const string DisplayName = "Claude Monitor";
    private const string Role = "Chief Operating Officer (COO)";
    private const string Profile = "P1";
    private const string Tool = "Claude Code";

    public Task<MonitorInfo> GetMonitorAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        var activeSubagentFiles = CountActiveSubagentFiles(paths.ClaudeHomePath, now);
        var sessionsDir = Path.Combine(paths.ClaudeHomePath, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            return Task.FromResult(Offline(now, "Claude sessions directory not found.", activeSubagentFiles));
        }

        var sessions = LoadSessionRecords(sessionsDir);
        if (sessions.Count == 0)
        {
            return Task.FromResult(Offline(now, "No active Claude session detected.", activeSubagentFiles));
        }

        var selected = sessions
            .OrderByDescending(s => string.Equals(s.Name, DisplayName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.UpdatedAtMs)
            .First();

        var busyExtraSessions = sessions.Count(s =>
            !string.Equals(s.SessionId, selected.SessionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Status, "busy", StringComparison.OrdinalIgnoreCase));
        var activeSubAgentCount = activeSubagentFiles + busyExtraSessions;

        var lastActivityUtc = selected.UpdatedAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(selected.UpdatedAtMs)
            : (DateTimeOffset?)null;

        var (model, rateLimitResetLabel) = InspectTranscriptTail(paths.ClaudeHomePath, selected);

        string state;
        if (!string.IsNullOrEmpty(rateLimitResetLabel) &&
            !string.Equals(selected.Status, "busy", StringComparison.OrdinalIgnoreCase))
        {
            state = "rate_limited";
        }
        else if (string.Equals(selected.Status, "busy", StringComparison.OrdinalIgnoreCase))
        {
            state = "busy";
        }
        else
        {
            state = "standby";
        }

        var notice = sessions.Count > 1
            ? $"{sessions.Count} active Claude sessions detected (target: 1)."
            : null;

        return Task.FromResult(new MonitorInfo(
            Id: MonitorId,
            DisplayName: DisplayName,
            Role: Role,
            Profile: Profile,
            Tool: Tool,
            State: state,
            Model: model,
            LastActivityUtc: lastActivityUtc,
            RateLimitResetLabel: rateLimitResetLabel,
            ActiveSessionCount: sessions.Count,
            AutomationStatus: null,
            Source: new SourceHealth("ok", now, notice),
            ActiveSubAgentCount: activeSubAgentCount));
    }

    private static MonitorInfo Offline(DateTimeOffset now, string notice, int activeSubAgentCount = 0)
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
            Source: new SourceHealth("unavailable", now, notice),
            ActiveSubAgentCount: activeSubAgentCount);
    }

    private static int CountActiveSubagentFiles(string claudeHomePath, DateTimeOffset nowUtc)
    {
        var projectsDir = Path.Combine(claudeHomePath, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return 0;
        }

        var cutoffUtc = nowUtc.UtcDateTime - SubAgentActiveWindow;
        var activeCount = 0;

        try
        {
            foreach (var subagentsDir in Directory.EnumerateDirectories(projectsDir, "subagents", SearchOption.AllDirectories))
            {
                foreach (var jsonlFile in Directory.EnumerateFiles(subagentsDir, "*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    if (!PathWhitelistGuard.IsAllowedFilePath(jsonlFile))
                    {
                        continue;
                    }

                    try
                    {
                        if (File.GetLastWriteTimeUtc(jsonlFile) >= cutoffUtc)
                        {
                            activeCount++;
                        }
                    }
                    catch
                    {
                        // Ignore transiently removed or locked subagent metadata.
                    }
                }
            }
        }
        catch
        {
            // Ignore directory enumeration races.
        }

        return activeCount;
    }

    private static List<ClaudeSessionRecord> LoadSessionRecords(string sessionsDir)
    {
        var results = new List<ClaudeSessionRecord>();
        foreach (var filePath in Directory.EnumerateFiles(sessionsDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (!PathWhitelistGuard.IsAllowedFilePath(filePath))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var sessionId = GetString(root, "sessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    continue;
                }

                var name = GetString(root, "name");
                var status = GetString(root, "status") ?? "idle";
                var cwd = GetString(root, "cwd");
                var updatedAt = GetInt64(root, "updatedAt") ?? GetInt64(root, "startedAt") ?? 0;

                results.Add(new ClaudeSessionRecord(sessionId, name, status, cwd, updatedAt));
            }
            catch
            {
                // Ignore transient half-written session files.
            }
        }

        return results;
    }

    private static (string? Model, string? RateLimitResetLabel) InspectTranscriptTail(
        string claudeHomePath,
        ClaudeSessionRecord session)
    {
        var transcriptPath = ResolveTranscriptPath(claudeHomePath, session);
        if (transcriptPath is null || !File.Exists(transcriptPath) || !PathWhitelistGuard.IsAllowedFilePath(transcriptPath))
        {
            return (null, null);
        }

        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > TailReadBytes)
            {
                stream.Seek(-TailReadBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var tailText = reader.ReadToEnd();
            var lines = tailText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            string? latestModel = null;
            string? latestRateLimitReset = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith('{'))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;

                    if (TryGetAssistantMessage(root, out var message))
                    {
                        var model = GetString(message, "model");
                        if (!string.IsNullOrWhiteSpace(model))
                        {
                            latestModel = model;
                        }

                        var limitMatch = ExtractRateLimitReset(message);
                        if (limitMatch is not null)
                        {
                            latestRateLimitReset = limitMatch;
                        }
                        else if (HasAssistantText(message))
                        {
                            // A subsequent normal assistant response clears any earlier rate-limit notice.
                            latestRateLimitReset = null;
                        }
                    }
                    else if (root.TryGetProperty("params", out var paramsEl) &&
                             paramsEl.ValueKind == JsonValueKind.Object)
                    {
                        var model = GetString(paramsEl, "model");
                        if (!string.IsNullOrWhiteSpace(model))
                        {
                            latestModel = model;
                        }
                    }
                }
                catch
                {
                    // Skip truncated first line from byte-seek tail or malformed line.
                }
            }

            return (latestModel, latestRateLimitReset);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? ResolveTranscriptPath(string claudeHomePath, ClaudeSessionRecord session)
    {
        var projectsDir = Path.Combine(claudeHomePath, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(session.Cwd))
        {
            var encoded = EncodeProjectFolder(session.Cwd);
            var candidate = Path.Combine(projectsDir, encoded, $"{session.SessionId}.jsonl");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var projectSubDir in Directory.EnumerateDirectories(projectsDir))
        {
            var candidate = Path.Combine(projectSubDir, $"{session.SessionId}.jsonl");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string EncodeProjectFolder(string cwd)
    {
        return cwd
            .Replace(":", "-", StringComparison.Ordinal)
            .Replace("\\", "-", StringComparison.Ordinal)
            .Replace("/", "-", StringComparison.Ordinal)
            .Replace(".", "-", StringComparison.Ordinal);
    }

    private static bool TryGetAssistantMessage(JsonElement root, out JsonElement message)
    {
        if (root.TryGetProperty("message", out message) &&
            message.ValueKind == JsonValueKind.Object &&
            string.Equals(GetString(message, "role"), "assistant", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        message = default;
        return false;
    }

    private static string? ExtractRateLimitReset(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !string.Equals(GetString(item, "type"), "text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = GetString(item, "text");
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var match = RateLimitResetRegex().Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static bool HasAssistantText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                string.Equals(GetString(item, "type"), "text", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(GetString(item, "text")))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static long? GetInt64(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) && prop.TryGetInt64(out var val)
            ? val
            : null;
    }

    [GeneratedRegex(@"(?:5-hour limit|usage limit|spend limit).*?resets\s+([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitResetRegex();

    private sealed record ClaudeSessionRecord(
        string SessionId,
        string? Name,
        string Status,
        string? Cwd,
        long UpdatedAtMs);
}
