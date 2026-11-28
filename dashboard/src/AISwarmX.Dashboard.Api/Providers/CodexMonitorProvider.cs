using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;
using AISwarmX.Dashboard.Api.Security;

namespace AISwarmX.Dashboard.Api.Providers;

/// <summary>
/// Reads standing AiSwarmX ChatGPT Codex (formerly MOBAi ChatGPT Codex / GPT Head / Codex Monitor) metadata read-only from
/// ~/.codex/state_5.sqlite, automation.toml, and session rollout tail telemetry (usage/rate-limit signals only).
/// </summary>
public sealed partial class CodexMonitorProvider
{
    private const int TailReadBytes = 64 * 1024;
    private const string MonitorId = "codex-monitor";
    private const string DisplayName = "AiSwarmX ChatGPT Codex";
    private const string LegacyHeadDisplayName = "GPT Head";
    private const string LegacyMonitorDisplayName = "Codex Monitor";
    private const string AiSwarmXNamespacePrefix = "AiSwarmX ";
    private const string MobaNamespacePrefix = "MOBAi ";
    private const string MobaAltPrefix = "MOBA ";
    private const string Role = "Technical Director & CPM";
    private const string Profile = "P2";
    private const string Tool = "ChatGPT Codex";
    private static readonly TimeSpan BusyThreshold = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SubAgentActiveWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RolloutLookbackWindow = TimeSpan.FromHours(12);

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

            var mobaThreads = new List<CodexThreadRecord>();
            var standingThreads = new List<CodexThreadRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                var model = reader.IsDBNull(2) ? null : reader.GetString(2);
                var updatedAtSeconds = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);

                if (IsForeignProjectNamedThread(name))
                {
                    continue;
                }

                var record = new CodexThreadRecord(id, name ?? string.Empty, model, updatedAtSeconds);
                mobaThreads.Add(record);
                if (IsMobaStandingThread(name))
                {
                    standingThreads.Add(record);
                }
            }

            var subAgentCutoffSeconds = now.Subtract(SubAgentActiveWindow).ToUnixTimeSeconds();

            if (standingThreads.Count == 0)
            {
                var fallbackThreads = mobaThreads
                    .Where(t => t.UpdatedAtSeconds >= subAgentCutoffSeconds)
                    .ToList();
                var fallbackByModel = CountThreadsByModel(fallbackThreads, "gpt-6.2-sol");
                return Task.FromResult(Unavailable(now, "ok", $"No active {DisplayName} thread found.", automationStatus, fallbackThreads.Count, fallbackByModel));
            }

            var selected = standingThreads
                .OrderByDescending(t => GetStandingPriorityRank(t.Name))
                .ThenByDescending(t => t.UpdatedAtSeconds)
                .First();

            var lastActivityUtc = selected.UpdatedAtSeconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(selected.UpdatedAtSeconds)
                : (DateTimeOffset?)null;

            var rateLimitResetLabel = DetectRateLimit(paths.CodexHomePath, selected.Id, lastActivityUtc, now);
            var isBusy = lastActivityUtc.HasValue && (now - lastActivityUtc.Value) <= BusyThreshold;
            var state = !string.IsNullOrEmpty(rateLimitResetLabel)
                ? "rate_limited"
                : isBusy
                    ? "busy"
                    : "standby";

            var activeSubThreads = mobaThreads
                .Where(t =>
                    !string.Equals(t.Id, selected.Id, StringComparison.Ordinal) &&
                    t.UpdatedAtSeconds >= subAgentCutoffSeconds)
                .ToList();
            var activeSubAgentCount = activeSubThreads.Count;
            var activeSubAgentsByModel = CountThreadsByModel(activeSubThreads, selected.Model ?? "gpt-6.2-sol");

            var notice = standingThreads.Count > 1
                ? $"{standingThreads.Count} non-archived AiSwarmX Codex threads active (target: 1)."
                : null;
            if (!string.IsNullOrEmpty(rateLimitResetLabel))
            {
                const string quotaNotice = "Quota pause detected from local Codex session telemetry.";
                notice = string.IsNullOrEmpty(notice) ? quotaNotice : $"{notice} {quotaNotice}";
            }

            return Task.FromResult(new MonitorInfo(
                Id: MonitorId,
                DisplayName: DisplayName,
                Role: Role,
                Profile: Profile,
                Tool: Tool,
                State: state,
                Model: selected.Model,
                LastActivityUtc: lastActivityUtc,
                RateLimitResetLabel: rateLimitResetLabel,
                ActiveSessionCount: standingThreads.Count,
                AutomationStatus: automationStatus,
                Source: new SourceHealth("ok", now, notice),
                ActiveSubAgentCount: activeSubAgentCount,
                ActiveSubAgentsByModel: activeSubAgentsByModel));
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

    private static bool IsMobaStandingThread(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) ||
            string.Equals(trimmed, "Guardian review", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.StartsWith(AiSwarmXNamespacePrefix, StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyHeadDisplayName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyMonitorDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForeignProjectNamedThread(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) ||
            string.Equals(trimmed, "Guardian review", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsMobaStandingThread(trimmed);
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
            return 7;
        }

        if (trimmed.StartsWith(AiSwarmXNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("Codex", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("GPT", StringComparison.OrdinalIgnoreCase)
                ? 6
                : 5;
        }

        if (trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("Codex", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("GPT", StringComparison.OrdinalIgnoreCase)
                ? 4
                : 3;
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

    private static IReadOnlyDictionary<string, int> CountThreadsByModel(
        IReadOnlyList<CodexThreadRecord> threads,
        string fallbackModel)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var thread in threads)
        {
            var (canonicalModel, _) = SubAgentModelCatalog.Normalize(thread.Model, fallbackModel);
            counts[canonicalModel] = counts.GetValueOrDefault(canonicalModel) + 1;
        }

        return counts;
    }

    private static MonitorInfo Unavailable(
        DateTimeOffset now,
        string sourceStatus,
        string notice,
        string? automationStatus,
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
            AutomationStatus: automationStatus,
            Source: new SourceHealth(sourceStatus, now, notice),
            ActiveSubAgentCount: activeSubAgentCount,
            ActiveSubAgentsByModel: activeSubAgentsByModel);
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

    private static string? DetectRateLimit(
        string codexHomePath,
        string selectedThreadId,
        DateTimeOffset? selectedActivityUtc,
        DateTimeOffset now)
    {
        var (selectedRolloutPath, newestRecentRolloutPath) = ResolveRolloutCandidates(codexHomePath, selectedThreadId, now);

        var selectedInspection = selectedRolloutPath is not null
            ? InspectRolloutTail(selectedRolloutPath, now)
            : default;

        var hasDistinctRecent = newestRecentRolloutPath is not null &&
                                !string.Equals(newestRecentRolloutPath, selectedRolloutPath, StringComparison.OrdinalIgnoreCase);
        var recentInspection = hasDistinctRecent
            ? InspectRolloutTail(newestRecentRolloutPath!, now)
            : default;

        if (selectedInspection.IsRateLimited)
        {
            if (hasDistinctRecent &&
                !recentInspection.IsRateLimited &&
                recentInspection.LatestSuccessUtc is { } recentSuccess &&
                selectedInspection.EventTimestampUtc is { } limitTime &&
                recentSuccess > limitTime)
            {
                return null;
            }

            if (hasDistinctRecent &&
                recentInspection.IsRateLimited &&
                recentInspection.EventTimestampUtc is { } newerLimit &&
                (!selectedInspection.EventTimestampUtc.HasValue || newerLimit > selectedInspection.EventTimestampUtc.Value))
            {
                return recentInspection.ResetLabel;
            }

            return selectedInspection.ResetLabel;
        }

        if (hasDistinctRecent && recentInspection.IsRateLimited)
        {
            var comparisonBaseline = selectedInspection.LatestSuccessUtc ?? selectedActivityUtc;
            if (!comparisonBaseline.HasValue ||
                !recentInspection.EventTimestampUtc.HasValue ||
                recentInspection.EventTimestampUtc.Value >= comparisonBaseline.Value.AddMinutes(-1))
            {
                return recentInspection.ResetLabel;
            }
        }

        return null;
    }

    private static (string? SelectedPath, string? NewestRecentPath) ResolveRolloutCandidates(
        string codexHomePath,
        string selectedThreadId,
        DateTimeOffset now)
    {
        var sessionsDir = Path.Combine(codexHomePath, "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            return (null, null);
        }

        string? selectedPath = null;
        var selectedWriteUtc = DateTime.MinValue;
        string? newestRecentPath = null;
        var newestWriteUtc = DateTime.MinValue;
        var recentCutoffUtc = now.UtcDateTime - RolloutLookbackWindow;
        var threadSuffix = $"{selectedThreadId}.jsonl";

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(sessionsDir, "*.jsonl", SearchOption.AllDirectories))
            {
                if (!PathWhitelistGuard.IsAllowedFilePath(filePath))
                {
                    continue;
                }

                DateTime writeUtc;
                try
                {
                    writeUtc = File.GetLastWriteTimeUtc(filePath);
                }
                catch
                {
                    continue;
                }

                var fileName = Path.GetFileName(filePath);
                if (!string.IsNullOrWhiteSpace(selectedThreadId) &&
                    fileName.EndsWith(threadSuffix, StringComparison.OrdinalIgnoreCase) &&
                    writeUtc >= selectedWriteUtc)
                {
                    selectedWriteUtc = writeUtc;
                    selectedPath = filePath;
                }

                if (writeUtc >= recentCutoffUtc && writeUtc >= newestWriteUtc)
                {
                    newestWriteUtc = writeUtc;
                    newestRecentPath = filePath;
                }
            }
        }
        catch
        {
            return (selectedPath, newestRecentPath);
        }

        return (selectedPath, newestRecentPath);
    }

    private static RolloutRateLimitInspection InspectRolloutTail(string rolloutPath, DateTimeOffset now)
    {
        if (!File.Exists(rolloutPath) || !PathWhitelistGuard.IsAllowedFilePath(rolloutPath))
        {
            return default;
        }

        try
        {
            using var stream = new FileStream(
                rolloutPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > TailReadBytes)
            {
                stream.Seek(-TailReadBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var tailText = reader.ReadToEnd();
            var lines = tailText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            long? latestResetsAt = null;
            LimitCandidate? taskErrorSignal = null;
            LimitCandidate? tokenLimitSignal = null;
            DateTimeOffset? latestSuccessUtc = null;

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
                    if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var payloadType = GetString(payload, "type");
                    var eventTs = ParseEventTimestampUtc(root, payload);

                    if (string.Equals(payloadType, "token_count", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessTokenCount(payload, eventTs, ref latestResetsAt, ref tokenLimitSignal);
                    }
                    else if (string.Equals(payloadType, "task_complete", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(payloadType, "turn_aborted", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(payloadType, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryExtractUsageLimitError(payload, latestResetsAt, out var resetLabel, out var resetsAt))
                        {
                            taskErrorSignal = new LimitCandidate(resetLabel, eventTs, resetsAt);
                        }
                        else if (string.Equals(payloadType, "task_complete", StringComparison.OrdinalIgnoreCase) &&
                                 !HasNonNullError(payload))
                        {
                            taskErrorSignal = null;
                            latestSuccessUtc = eventTs ?? latestSuccessUtc;
                        }
                    }
                }
                catch
                {
                    // Skip truncated first line from byte-seek tail or malformed JSONL line.
                }
            }

            DateTimeOffset? fileFallbackTs = null;
            try
            {
                fileFallbackTs = new DateTimeOffset(File.GetLastWriteTimeUtc(rolloutPath), TimeSpan.Zero);
            }
            catch
            {
                // Ignore mtime failure on transient rollout files.
            }

            var nowUnixSeconds = now.ToUnixTimeSeconds();
            if (taskErrorSignal is not null)
            {
                if (taskErrorSignal.ResetsAtUnixSeconds is { } errorResetAt && nowUnixSeconds >= errorResetAt)
                {
                    return new RolloutRateLimitInspection(false, null, taskErrorSignal.TimestampUtc ?? fileFallbackTs, latestSuccessUtc);
                }

                return new RolloutRateLimitInspection(
                    true,
                    taskErrorSignal.ResetLabel,
                    taskErrorSignal.TimestampUtc ?? fileFallbackTs,
                    latestSuccessUtc);
            }

            if (tokenLimitSignal is not null &&
                (!tokenLimitSignal.ResetsAtUnixSeconds.HasValue || nowUnixSeconds < tokenLimitSignal.ResetsAtUnixSeconds.Value))
            {
                return new RolloutRateLimitInspection(
                    true,
                    tokenLimitSignal.ResetLabel,
                    tokenLimitSignal.TimestampUtc ?? fileFallbackTs,
                    latestSuccessUtc);
            }

            return new RolloutRateLimitInspection(false, null, fileFallbackTs, latestSuccessUtc);
        }
        catch
        {
            return default;
        }
    }

    private static void ProcessTokenCount(
        JsonElement payload,
        DateTimeOffset? eventTs,
        ref long? latestResetsAt,
        ref LimitCandidate? tokenLimitSignal)
    {
        if (!payload.TryGetProperty("rate_limits", out var rateLimits) || rateLimits.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var (primaryUsed, primaryResetsAt) = ReadQuotaWindow(rateLimits, "primary");
        var (secondaryUsed, secondaryResetsAt) = ReadQuotaWindow(rateLimits, "secondary");

        var preferredResetsAt = secondaryUsed is >= 100 && primaryUsed is not >= 100
            ? secondaryResetsAt ?? primaryResetsAt
            : primaryResetsAt ?? secondaryResetsAt;
        if (preferredResetsAt.HasValue)
        {
            latestResetsAt = preferredResetsAt;
        }

        var reachedType = GetString(rateLimits, "rate_limit_reached_type");
        var hasUnlimitedOrCredits = HasPaidCredits(rateLimits);
        var quotaExhausted = !string.IsNullOrWhiteSpace(reachedType) ||
                             (!hasUnlimitedOrCredits && (primaryUsed is >= 100 || secondaryUsed is >= 100));

        if (quotaExhausted)
        {
            var effectiveReset = preferredResetsAt ?? latestResetsAt;
            var label = effectiveReset.HasValue
                ? FormatResetUnixLabel(effectiveReset.Value)
                : "Usage limit exceeded";
            tokenLimitSignal = new LimitCandidate(label, eventTs, effectiveReset);
        }
        else if (primaryUsed is < 100 && (!secondaryUsed.HasValue || secondaryUsed.Value < 100))
        {
            tokenLimitSignal = null;
        }
    }

    private static bool TryExtractUsageLimitError(
        JsonElement payload,
        long? fallbackResetsAt,
        out string resetLabel,
        out long? resetsAt)
    {
        resetsAt = fallbackResetsAt;
        resetLabel = string.Empty;

        string? errorInfo = null;
        string? message = null;

        if (payload.TryGetProperty("error", out var errorObj) && errorObj.ValueKind == JsonValueKind.Object)
        {
            errorInfo = GetString(errorObj, "codex_error_info");
            message = GetString(errorObj, "message");
        }
        else if (payload.TryGetProperty("error", out var errorStr) && errorStr.ValueKind == JsonValueKind.String)
        {
            message = errorStr.GetString();
        }
        else
        {
            message = GetString(payload, "message") ?? GetString(payload, "reason");
        }

        var isUsageLimitInfo = !string.IsNullOrWhiteSpace(errorInfo) &&
                               (errorInfo.Contains("usage_limit", StringComparison.OrdinalIgnoreCase) ||
                                errorInfo.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
                                errorInfo.Contains("quota", StringComparison.OrdinalIgnoreCase));
        var isUsageLimitMsg = !string.IsNullOrWhiteSpace(message) && UsageLimitMessageRegex().IsMatch(message);

        if (!isUsageLimitInfo && !isUsageLimitMsg)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            var tryAgainMatch = TryAgainAtRegex().Match(message);
            if (tryAgainMatch.Success)
            {
                resetLabel = $"{tryAgainMatch.Groups["reset"].Value.Trim()} · usage limit exceeded";
                return true;
            }
        }

        resetLabel = fallbackResetsAt.HasValue
            ? FormatResetUnixLabel(fallbackResetsAt.Value)
            : "Usage limit exceeded";
        return true;
    }

    private static bool HasNonNullError(JsonElement payload)
    {
        return payload.TryGetProperty("error", out var errorProp) && errorProp.ValueKind != JsonValueKind.Null;
    }

    private static (double? UsedPercent, long? ResetsAt) ReadQuotaWindow(JsonElement rateLimits, string windowName)
    {
        if (!rateLimits.TryGetProperty(windowName, out var windowEl) || windowEl.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        double? usedPercent = windowEl.TryGetProperty("used_percent", out var usedEl) && usedEl.TryGetDouble(out var usedVal)
            ? usedVal
            : null;
        long? resetsAt = windowEl.TryGetProperty("resets_at", out var resetEl) && resetEl.TryGetInt64(out var resetVal)
            ? resetVal
            : null;

        return (usedPercent, resetsAt);
    }

    private static bool HasPaidCredits(JsonElement rateLimits)
    {
        if (!rateLimits.TryGetProperty("credits", out var creditsEl) || creditsEl.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hasCredits = creditsEl.TryGetProperty("has_credits", out var hc) && hc.ValueKind == JsonValueKind.True;
        var unlimited = creditsEl.TryGetProperty("unlimited", out var ul) && ul.ValueKind == JsonValueKind.True;
        return hasCredits || unlimited;
    }

    private static string FormatResetUnixLabel(long unixSeconds)
    {
        var local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
        return $"{local.ToString("HH:mm", CultureInfo.InvariantCulture)} · usage limit exceeded";
    }

    private static DateTimeOffset? ParseEventTimestampUtc(JsonElement root, JsonElement payload)
    {
        if (payload.TryGetProperty("completed_at", out var completedEl) && completedEl.TryGetInt64(out var completedSec) && completedSec > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(completedSec);
        }

        var tsText = GetString(root, "timestamp");
        if (!string.IsNullOrWhiteSpace(tsText) &&
            DateTimeOffset.TryParse(tsText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? GetString(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    [GeneratedRegex("^status\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex TomlStatusRegex();

    [GeneratedRegex("^rrule\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex TomlRruleRegex();

    [GeneratedRegex(@"(?:hit your usage limit|usage_limit_exceeded|rate[- ]limit|usage limit|quota exceeded)", RegexOptions.IgnoreCase)]
    private static partial Regex UsageLimitMessageRegex();

    [GeneratedRegex(@"try again at\s+(?<reset>[^.\r\n""]+?)(?:\s*[.""]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TryAgainAtRegex();

    private sealed record CodexThreadRecord(string Id, string Name, string? Model, long UpdatedAtSeconds);
    private sealed record LimitCandidate(string ResetLabel, DateTimeOffset? TimestampUtc, long? ResetsAtUnixSeconds);
    private readonly record struct RolloutRateLimitInspection(
        bool IsRateLimited,
        string? ResetLabel,
        DateTimeOffset? EventTimestampUtc,
        DateTimeOffset? LatestSuccessUtc);
}
