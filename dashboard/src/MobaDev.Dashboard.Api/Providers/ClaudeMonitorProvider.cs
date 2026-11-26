using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads standing MOBAi Claude Opus (formerly Claude Head / Claude Monitor) metadata from ~/.claude/sessions/*.json
/// and only model/rate-limit metadata from the tail of the active session transcript without ever exposing chat text.
/// </summary>
public sealed partial class ClaudeMonitorProvider
{
    private const int TailReadBytes = 64 * 1024;
    private static readonly TimeSpan SubAgentActiveWindow = TimeSpan.FromMinutes(2);
    private const string MonitorId = "claude-monitor";
    private const string DisplayName = "MOBAi Claude Opus";
    private const string LegacyHeadDisplayName = "Claude Head";
    private const string LegacyMonitorDisplayName = "Claude Monitor";
    private const string MobaNamespacePrefix = "MOBAi ";
    private const string MobaAltPrefix = "MOBA ";
    private const string Role = "Chief Operating Officer (COO)";
    private const string Profile = "P1";
    private const string Tool = "Claude Code";

    private readonly TimeProvider _timeProvider;

    public ClaudeMonitorProvider()
        : this(TimeProvider.System)
    {
    }

    internal ClaudeMonitorProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<MonitorInfo> GetMonitorAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        return GetMonitorAsync(
            paths,
            TimeSpan.FromMinutes(DashboardOptions.DefaultBusyIdleAfterMinutes),
            cancellationToken);
    }

    public Task<MonitorInfo> GetMonitorAsync(
        ResolvedDashboardPaths paths,
        TimeSpan busyIdleThreshold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveThreshold = MonitorStateRules.NormalizeThreshold(busyIdleThreshold);
        var now = _timeProvider.GetUtcNow();

        var activeSubagentModels = CollectActiveSubagentFileModels(paths.ClaudeHomePath, now);
        var sessionsDir = Path.Combine(paths.ClaudeHomePath, "sessions");
        var rawSessions = Directory.Exists(sessionsDir)
            ? LoadSessionRecords(sessionsDir)
            : [];
        var sessions = FilterMobaSessions(rawSessions, paths);

        if (sessions.Count == 0)
        {
            var offlineByModel = GroupModels(activeSubagentModels, "claude-opus-5-5");
            if (TryCreateClosedQuotaPauseMonitor(paths.ClaudeHomePath, now, activeSubagentModels.Count, offlineByModel, out var pausedMonitor))
            {
                return Task.FromResult(pausedMonitor);
            }

            var noticeText = Directory.Exists(sessionsDir)
                ? $"No active {DisplayName} session detected."
                : "Claude sessions directory not found.";
            return Task.FromResult(Offline(now, noticeText, activeSubagentModels.Count, offlineByModel));
        }

        var selected = sessions
            .OrderByDescending(s => GetStandingPriorityRank(s.Name))
            .ThenByDescending(s => s.UpdatedAtMs)
            .First();

        var (model, rateLimitResetLabel) = InspectTranscriptTail(paths.ClaudeHomePath, selected);
        var fallbackClaudeModel = model ?? "claude-opus-5-5";

        var busyExtraSessionRecords = sessions
            .Where(s =>
                !string.Equals(s.SessionId, selected.SessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Status, "busy", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var allSubModels = new List<string?>(activeSubagentModels.Count + busyExtraSessionRecords.Count);
        allSubModels.AddRange(activeSubagentModels);
        foreach (var extraSession in busyExtraSessionRecords)
        {
            var (extraModel, _) = InspectTranscriptTail(paths.ClaudeHomePath, extraSession);
            allSubModels.Add(extraModel ?? fallbackClaudeModel);
        }

        var activeSubAgentCount = allSubModels.Count;
        var activeSubAgentsByModel = GroupModels(allSubModels, fallbackClaudeModel);

        var lastActivityUtc = selected.UpdatedAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(selected.UpdatedAtMs)
            : (DateTimeOffset?)null;

        var isBusy = string.Equals(selected.Status, "busy", StringComparison.OrdinalIgnoreCase);
        var (state, effectiveRateLimitResetLabel) = MonitorStateRules.ResolveState(
            isBusy,
            rateLimitResetLabel,
            lastActivityUtc,
            now,
            effectiveThreshold);

        var notice = sessions.Count > 1
            ? $"{sessions.Count} active MOBAi Claude sessions detected (target: 1)."
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
            RateLimitResetLabel: effectiveRateLimitResetLabel,
            ActiveSessionCount: sessions.Count,
            AutomationStatus: null,
            Source: new SourceHealth("ok", now, notice),
            ActiveSubAgentCount: activeSubAgentCount,
            ActiveSubAgentsByModel: activeSubAgentsByModel));
    }

    private static IReadOnlyDictionary<string, int> GroupModels(IReadOnlyList<string?> rawModels, string fallbackModel)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawModels)
        {
            var (canonicalModel, _) = SubAgentModelCatalog.Normalize(raw, fallbackModel);
            counts[canonicalModel] = counts.GetValueOrDefault(canonicalModel) + 1;
        }

        return counts;
    }

    private static MonitorInfo Offline(
        DateTimeOffset now,
        string notice,
        int activeSubAgentCount = 0,
        IReadOnlyDictionary<string, int>? activeSubAgentsByModel = null)
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
            ActiveSubAgentCount: activeSubAgentCount,
            ActiveSubAgentsByModel: activeSubAgentsByModel);
    }

    private static List<string?> CollectActiveSubagentFileModels(string claudeHomePath, DateTimeOffset nowUtc)
    {
        var projectsDir = Path.Combine(claudeHomePath, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return [];
        }

        var cutoffUtc = nowUtc.UtcDateTime - SubAgentActiveWindow;
        var models = new List<string?>();

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
                            var (subModel, _) = InspectTranscriptTailFromFile(jsonlFile);
                            models.Add(subModel);
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

        return models;
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

    private static bool TryCreateClosedQuotaPauseMonitor(
        string claudeHomePath,
        DateTimeOffset now,
        int activeSubAgentCount,
        IReadOnlyDictionary<string, int> activeSubAgentsByModel,
        out MonitorInfo monitor)
    {
        var latestTranscript = ResolveLatestProjectTranscript(claudeHomePath);
        if (latestTranscript is not null)
        {
            var (model, rateLimitResetLabel) = InspectTranscriptTailFromFile(latestTranscript);
            if (!string.IsNullOrEmpty(rateLimitResetLabel))
            {
                DateTimeOffset? lastActivityUtc = null;
                try
                {
                    lastActivityUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(latestTranscript), TimeSpan.Zero);
                }
                catch
                {
                    // Ignore timestamp lookup failure on transient transcript files.
                }

                monitor = new MonitorInfo(
                    Id: MonitorId,
                    DisplayName: DisplayName,
                    Role: Role,
                    Profile: Profile,
                    Tool: Tool,
                    State: "rate_limited",
                    Model: model,
                    LastActivityUtc: lastActivityUtc,
                    RateLimitResetLabel: rateLimitResetLabel,
                    ActiveSessionCount: 0,
                    AutomationStatus: null,
                    Source: new SourceHealth("ok", now, "Claude session closed during active quota pause."),
                    ActiveSubAgentCount: activeSubAgentCount,
                    ActiveSubAgentsByModel: activeSubAgentsByModel);
                return true;
            }
        }

        monitor = default!;
        return false;
    }

    private static string? ResolveLatestProjectTranscript(string claudeHomePath)
    {
        var projectsDir = Path.Combine(claudeHomePath, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return null;
        }

        string? latestFile = null;
        var latestWriteUtc = DateTime.MinValue;

        try
        {
            foreach (var projectSubDir in Directory.EnumerateDirectories(projectsDir))
            {
                foreach (var jsonlFile in Directory.EnumerateFiles(projectSubDir, "*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    if (!PathWhitelistGuard.IsAllowedFilePath(jsonlFile))
                    {
                        continue;
                    }

                    try
                    {
                        var writeUtc = File.GetLastWriteTimeUtc(jsonlFile);
                        if (writeUtc > latestWriteUtc)
                        {
                            latestWriteUtc = writeUtc;
                            latestFile = jsonlFile;
                        }
                    }
                    catch
                    {
                        // Ignore unreadable transcript metadata.
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return latestFile;
    }

    private static (string? Model, string? RateLimitResetLabel) InspectTranscriptTail(
        string claudeHomePath,
        ClaudeSessionRecord session)
    {
        return InspectTranscriptTailFromFile(ResolveTranscriptPath(claudeHomePath, session));
    }

    private static (string? Model, string? RateLimitResetLabel) InspectTranscriptTailFromFile(string? transcriptPath)
    {
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
                var captured = match.Groups["resetEn"].Success
                    ? match.Groups["resetEn"].Value
                    : match.Groups["resetDe"].Value;
                return captured.Trim();
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

    private static List<ClaudeSessionRecord> FilterMobaSessions(
        IReadOnlyList<ClaudeSessionRecord> rawSessions,
        ResolvedDashboardPaths paths)
    {
        var filtered = new List<ClaudeSessionRecord>(rawSessions.Count);
        foreach (var session in rawSessions)
        {
            var trimmedName = session.Name?.Trim();
            if (!string.IsNullOrEmpty(trimmedName))
            {
                if (IsMobaSessionName(trimmedName))
                {
                    filtered.Add(session);
                }

                continue;
            }

            // Unnamed sessions belong to MOBA only when cwd is empty (legacy/tests) or inside a MOBA repo/worktree.
            if (string.IsNullOrWhiteSpace(session.Cwd) ||
                IsPathUnder(session.Cwd, paths.MobaFlowRepoPath) ||
                IsPathUnder(session.Cwd, paths.MobaDevRepoPath))
            {
                filtered.Add(session);
            }
        }

        return filtered;
    }

    private static bool IsMobaSessionName(string trimmedName)
    {
        return trimmedName.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase) ||
               trimmedName.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, LegacyHeadDisplayName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, LegacyMonitorDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetStandingPriorityRank(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return 0;
        }

        if (string.Equals(trimmed, DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Contains("Claude", StringComparison.OrdinalIgnoreCase) ? 4 : 3;
        }

        if (trimmed.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(trimmed, LegacyHeadDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, LegacyMonitorDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static bool IsPathUnder(string candidatePath, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            var normalizedCandidate = Path.GetFullPath(candidatePath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRoot = Path.GetFullPath(rootPath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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

    [GeneratedRegex(
        @"(?:(?:5-hour limit|weekly limit|monthly limit|usage limit|spend limit).*?resets\s+(?<resetEn>[^\r\n]+)|(?:Ausgabenlimit|Nutzungslimit).*?wird\s+am\s+(?<resetDe>[^\r\n]+?)(?=\s+zurückgesetzt|\r|\n|$))",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RateLimitResetRegex();

    private sealed record ClaudeSessionRecord(
        string SessionId,
        string? Name,
        string Status,
        string? Cwd,
        long UpdatedAtMs);
}
