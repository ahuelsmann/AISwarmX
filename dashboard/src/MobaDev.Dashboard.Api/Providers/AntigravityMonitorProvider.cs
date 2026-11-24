using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads standing MOBAi Antigravity Root Head metadata (MOBAi Antigravity Gemini &amp; MOBAi Antigravity Opus)
/// read-only from ~/.gemini/antigravity/conversation_summaries.db and transcript tail model tags.
/// </summary>
public sealed partial class AntigravityMonitorProvider
{
    private const int TailReadBytes = 512 * 1024;
    private const string ArgonMonitorId = "antigravity-monitor";
    private const string ArgonDisplayName = "MOBAi Antigravity Gemini";
    private const string LegacyArgonHeadName = "Argon Head";
    private const string LegacyDisplayName = "Antigravity Monitor";
    private const string MobaNamespacePrefix = "MOBAi ";
    private const string MobaAltPrefix = "MOBA ";
    private const string ArgonRole = "Head of AI Strategy";
    private const string ArgonProfile = "P3";
    private const string DefaultArgonModel = "Orchestrator · Gemini 4 Argon";
    private const string LegacyDesktopModel = "Orchestrator (Neon)";

    private const string OpusMonitorId = "antigravity-opus-head";
    private const string OpusDisplayName = "MOBAi Antigravity Opus";
    private const string LegacyOpusHeadName = "Opus Head";
    private const string OpusRole = "Acting COO & Opus Orchestrator";
    private const string OpusProfile = "P1";
    private const string DefaultOpusModel = "Orchestrator · Claude Opus 5.5";

    private const string Tool = "Antigravity";

    public async Task<MonitorInfo> GetMonitorAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        var monitors = await GetMonitorsAsync(paths, cancellationToken).ConfigureAwait(false);
        return monitors[0];
    }

    public Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        var dbPath = Path.Combine(paths.AntigravityHomePath, "conversation_summaries.db");
        if (!File.Exists(dbPath))
        {
            IReadOnlyList<MonitorInfo> missing =
            [
                Unavailable(ArgonMonitorId, ArgonDisplayName, ArgonRole, ArgonProfile, now, "unavailable", "Antigravity database (conversation_summaries.db) not found.", defaultModel: null),
                Unavailable(OpusMonitorId, OpusDisplayName, OpusRole, OpusProfile, now, "unavailable", "Antigravity database (conversation_summaries.db) not found.", defaultModel: DefaultOpusModel)
            ];
            return Task.FromResult(missing);
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
            var foreignRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mobaDesktopConversations = new List<AntigravityConversationRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var status = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var lastModifiedRaw = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var source = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

                var isCliSource = string.Equals(source, "CORTEX_TRAJECTORY_SOURCE_CLI", StringComparison.OrdinalIgnoreCase);
                if (!isCliSource && !string.IsNullOrWhiteSpace(title) && !IsMobaAntigravityTitle(title))
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        foreignRootIds.Add(id);
                    }

                    continue;
                }

                if (string.Equals(status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(id))
                {
                    runningConversationIds.Add(id);
                }

                if (isCliSource || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                mobaDesktopConversations.Add(new AntigravityConversationRecord(id, title, status, lastModifiedRaw));
            }

            if (mobaDesktopConversations.Count == 0)
            {
                var scopedRunning = runningConversationIds.Where(id => !foreignRootIds.Contains(id)).ToList();
                var emptySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var (emptyArgonByModel, _) = ClassifyAntigravitySubAgents(
                    paths.AntigravityHomePath,
                    scopedRunning,
                    hasArgonHead: true,
                    hasOpusHead: false,
                    emptySet,
                    emptySet);
                IReadOnlyList<MonitorInfo> empty =
                [
                    Unavailable(ArgonMonitorId, ArgonDisplayName, ArgonRole, ArgonProfile, now, "ok", $"No {ArgonDisplayName} session found.", scopedRunning.Count, activeSubAgentsByModel: emptyArgonByModel),
                    Unavailable(OpusMonitorId, OpusDisplayName, OpusRole, OpusProfile, now, "ok", $"No {OpusDisplayName} session found.", defaultModel: DefaultOpusModel)
                ];
                return Task.FromResult(empty);
            }

            var opusSelected = mobaDesktopConversations
                .Where(c => IsOpusCandidateTitle(c.Title))
                .OrderByDescending(c => GetOpusPriorityRank(c.Title))
                .FirstOrDefault();

            var argonSelected = mobaDesktopConversations
                .Where(c => opusSelected is null || !string.Equals(c.Id, opusSelected.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => GetGeminiPriorityRank(c.Title))
                .FirstOrDefault();

            var rootIds = new HashSet<string>(foreignRootIds, StringComparer.OrdinalIgnoreCase);
            if (argonSelected is not null && !string.IsNullOrWhiteSpace(argonSelected.Id))
            {
                rootIds.Add(argonSelected.Id);
            }

            if (opusSelected is not null && !string.IsNullOrWhiteSpace(opusSelected.Id))
            {
                rootIds.Add(opusSelected.Id);
            }

            var argonSpawnedIds = ExtractSpawnedSubAgentIds(paths.AntigravityHomePath, argonSelected?.Id);
            var opusSpawnedIds = ExtractSpawnedSubAgentIds(paths.AntigravityHomePath, opusSelected?.Id);
            var subAgentIds = runningConversationIds.Where(id => !rootIds.Contains(id)).ToList();
            var (argonSubAgentsByModel, opusSubAgentsByModel) = ClassifyAntigravitySubAgents(
                paths.AntigravityHomePath,
                subAgentIds,
                hasArgonHead: argonSelected is not null,
                hasOpusHead: opusSelected is not null,
                argonSpawnedIds,
                opusSpawnedIds);
            var argonSubAgentCount = argonSubAgentsByModel.Values.Sum();
            var opusSubAgentCount = opusSubAgentsByModel.Values.Sum();

            var extraSessionCount = Math.Max(0, mobaDesktopConversations.Count - (argonSelected is not null ? 1 : 0) - (opusSelected is not null ? 1 : 0));
            var extraNotice = mobaDesktopConversations.Count > 2
                ? $"{mobaDesktopConversations.Count} MOBAi Antigravity sessions found in summaries (target: 2: {ArgonDisplayName} + {OpusDisplayName})."
                : null;

            MonitorInfo argonInfo;
            if (argonSelected is not null)
            {
                var legacyMatched = string.Equals(argonSelected.Title.Trim(), LegacyDisplayName, StringComparison.OrdinalIgnoreCase);
                var fallbackModel = legacyMatched ? LegacyDesktopModel : DefaultArgonModel;
                var model = InspectConversationModel(paths.AntigravityHomePath, argonSelected.Id, fallbackModel);
                argonInfo = BuildMonitorInfo(
                    ArgonMonitorId,
                    ArgonDisplayName,
                    ArgonRole,
                    ArgonProfile,
                    argonSelected,
                    model,
                    activeSessionCount: 1 + extraSessionCount,
                    argonSubAgentCount,
                    argonSubAgentsByModel,
                    now,
                    extraNotice);
            }
            else
            {
                argonInfo = Unavailable(
                    ArgonMonitorId,
                    ArgonDisplayName,
                    ArgonRole,
                    ArgonProfile,
                    now,
                    "ok",
                    $"No {ArgonDisplayName} session found.",
                    activeSubAgentCount: 0);
            }

            MonitorInfo opusInfo;
            if (opusSelected is not null)
            {
                var model = InspectConversationModel(paths.AntigravityHomePath, opusSelected.Id, DefaultOpusModel);
                opusInfo = BuildMonitorInfo(
                    OpusMonitorId,
                    OpusDisplayName,
                    OpusRole,
                    OpusProfile,
                    opusSelected,
                    model,
                    activeSessionCount: 1,
                    activeSubAgentCount: opusSubAgentCount,
                    activeSubAgentsByModel: opusSubAgentsByModel,
                    now,
                    notice: null);
            }
            else
            {
                opusInfo = Unavailable(
                    OpusMonitorId,
                    OpusDisplayName,
                    OpusRole,
                    OpusProfile,
                    now,
                    "ok",
                    $"No {OpusDisplayName} session found.",
                    activeSubAgentCount: 0,
                    defaultModel: DefaultOpusModel);
            }

            IReadOnlyList<MonitorInfo> result = [argonInfo, opusInfo];
            return Task.FromResult(result);
        }
        catch (SqliteException ex) when (SqliteReadOnlyHelper.IsBusyOrLocked(ex))
        {
            IReadOnlyList<MonitorInfo> locked =
            [
                Unavailable(ArgonMonitorId, ArgonDisplayName, ArgonRole, ArgonProfile, now, "degraded", "Database temporarily locked — retrying on next poll."),
                Unavailable(OpusMonitorId, OpusDisplayName, OpusRole, OpusProfile, now, "degraded", "Database temporarily locked — retrying on next poll.", defaultModel: DefaultOpusModel)
            ];
            return Task.FromResult(locked);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            IReadOnlyList<MonitorInfo> failed =
            [
                Unavailable(ArgonMonitorId, ArgonDisplayName, ArgonRole, ArgonProfile, now, "unavailable", $"Unable to read Antigravity status: {ex.Message}"),
                Unavailable(OpusMonitorId, OpusDisplayName, OpusRole, OpusProfile, now, "unavailable", $"Unable to read Antigravity status: {ex.Message}", defaultModel: DefaultOpusModel)
            ];
            return Task.FromResult(failed);
        }
    }

    private static (IReadOnlyDictionary<string, int> ArgonByModel, IReadOnlyDictionary<string, int> OpusByModel) ClassifyAntigravitySubAgents(
        string antigravityHomePath,
        IReadOnlyList<string> subAgentIds,
        bool hasArgonHead,
        bool hasOpusHead,
        IReadOnlySet<string> argonSpawnedIds,
        IReadOnlySet<string> opusSpawnedIds)
    {
        var argonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var opusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var subId in subAgentIds)
        {
            var rawModel = InspectConversationModel(antigravityHomePath, subId, DefaultArgonModel);
            var (canonicalModel, _) = SubAgentModelCatalog.Normalize(rawModel, DefaultArgonModel);

            bool assignToOpus;
            if (opusSpawnedIds.Contains(subId) && !argonSpawnedIds.Contains(subId))
            {
                assignToOpus = true;
            }
            else if (argonSpawnedIds.Contains(subId))
            {
                assignToOpus = false;
            }
            else
            {
                assignToOpus = hasOpusHead && (SubAgentModelCatalog.IsClaudeModel(canonicalModel) || !hasArgonHead);
            }

            var target = assignToOpus ? opusCounts : argonCounts;
            target[canonicalModel] = target.GetValueOrDefault(canonicalModel) + 1;
        }

        return (argonCounts, opusCounts);
    }

    private static MonitorInfo BuildMonitorInfo(
        string id,
        string displayName,
        string role,
        string profile,
        AntigravityConversationRecord selected,
        string model,
        int activeSessionCount,
        int activeSubAgentCount,
        IReadOnlyDictionary<string, int> activeSubAgentsByModel,
        DateTimeOffset now,
        string? notice)
    {
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

        return new MonitorInfo(
            Id: id,
            DisplayName: displayName,
            Role: role,
            Profile: profile,
            Tool: Tool,
            State: state,
            Model: model,
            LastActivityUtc: lastActivityUtc,
            RateLimitResetLabel: null,
            ActiveSessionCount: activeSessionCount,
            AutomationStatus: null,
            Source: new SourceHealth("ok", now, notice),
            ActiveSubAgentCount: activeSubAgentCount,
            ActiveSubAgentsByModel: activeSubAgentsByModel);
    }

    private static HashSet<string> ExtractSpawnedSubAgentIds(string antigravityHomePath, string? rootConversationId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tail = ReadTranscriptTailText(antigravityHomePath, rootConversationId);
        if (string.IsNullOrEmpty(tail))
        {
            return ids;
        }

        foreach (Match match in SpawnedConversationIdRegex().Matches(tail))
        {
            var id = match.Groups["id"].Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    internal static string InspectConversationModel(string antigravityHomePath, string conversationId, string fallbackModel)
    {
        var tail = ReadTranscriptTailText(antigravityHomePath, conversationId);
        if (string.IsNullOrEmpty(tail))
        {
            return fallbackModel;
        }

        string? primaryModel = null;
        string? lastAnyModel = null;
        foreach (Match match in ModelFieldRegex().Matches(tail))
        {
            var candidate = match.Groups["m"].Value;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            lastAnyModel = candidate;
            // Antigravity Orchestrator runs planning turns on the chosen Head model (e.g. Carbon/Argon or Opus)
            // and inner execution steps on gemini-4-hydrogen-xhigh; prefer the primary Head model.
            if (!candidate.Contains("hydrogen", StringComparison.OrdinalIgnoreCase))
            {
                primaryModel = candidate;
            }
        }

        var selectedModel = primaryModel ?? lastAnyModel;
        return string.IsNullOrWhiteSpace(selectedModel)
            ? fallbackModel
            : FormatAntigravityModel(selectedModel, fallbackModel);
    }

    private static string? ReadTranscriptTailText(string antigravityHomePath, string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(antigravityHomePath) || string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var transcriptPath = Path.Combine(
            antigravityHomePath,
            "brain",
            conversationId,
            ".system_generated",
            "logs",
            "transcript.jsonl");

        if (!File.Exists(transcriptPath) || !PathWhitelistGuard.IsAllowedFilePath(transcriptPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            var offset = Math.Max(0, length - TailReadBytes);
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[(int)(length - offset)];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string FormatAntigravityModel(string rawModel, string fallbackModel)
    {
        var normalized = rawModel.Trim().ToLowerInvariant();
        if (normalized.Contains("gpt-oss", StringComparison.Ordinal) ||
            normalized.Contains("120b", StringComparison.Ordinal))
        {
            return "GPT-OSS 120B";
        }

        if (normalized.Contains("opus", StringComparison.Ordinal))
        {
            return "Orchestrator · Claude Opus 5.5";
        }

        if (normalized.Contains("sonnet", StringComparison.Ordinal))
        {
            return "Orchestrator · Claude Sonnet 5.5";
        }

        if (normalized.Contains("carbon", StringComparison.Ordinal))
        {
            return "Orchestrator · Gemini 4 Carbon";
        }

        if (normalized.Contains("argon", StringComparison.Ordinal))
        {
            return "Orchestrator · Gemini 4 Argon";
        }

        if (normalized.Contains("hydrogen", StringComparison.Ordinal))
        {
            return "Orchestrator · Gemini 4 Hydrogen";
        }

        if (normalized.Contains("neon", StringComparison.Ordinal))
        {
            return LegacyDesktopModel;
        }

        return string.IsNullOrWhiteSpace(rawModel) ? fallbackModel : rawModel;
    }

    private static bool IsMobaAntigravityTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        return trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyArgonHeadName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyDisplayName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyOpusHeadName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpusCandidateTitle(string title)
    {
        var trimmed = title.Trim();
        return string.Equals(trimmed, OpusDisplayName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyOpusHeadName, StringComparison.OrdinalIgnoreCase) ||
               ((trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase)) &&
                trimmed.Contains("Opus", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetOpusPriorityRank(string title)
    {
        var trimmed = title.Trim();
        if (string.Equals(trimmed, OpusDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (trimmed.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return string.Equals(trimmed, LegacyOpusHeadName, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static int GetGeminiPriorityRank(string title)
    {
        var trimmed = title.Trim();
        if (string.Equals(trimmed, ArgonDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (trimmed.StartsWith(MobaNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("Carbon", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("Argon", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("Neon", StringComparison.OrdinalIgnoreCase)
                ? 4
                : 3;
        }

        if (trimmed.StartsWith(MobaAltPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return string.Equals(trimmed, LegacyArgonHeadName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, LegacyDisplayName, StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }

    private static MonitorInfo Unavailable(
        string id,
        string displayName,
        string role,
        string profile,
        DateTimeOffset now,
        string sourceStatus,
        string notice,
        int activeSubAgentCount = 0,
        string? defaultModel = null,
        IReadOnlyDictionary<string, int>? activeSubAgentsByModel = null)
    {
        return new MonitorInfo(
            Id: id,
            DisplayName: displayName,
            Role: role,
            Profile: profile,
            Tool: Tool,
            State: "offline",
            Model: defaultModel,
            LastActivityUtc: null,
            RateLimitResetLabel: null,
            ActiveSessionCount: 0,
            AutomationStatus: null,
            Source: new SourceHealth(sourceStatus, now, notice),
            ActiveSubAgentCount: activeSubAgentCount,
            ActiveSubAgentsByModel: activeSubAgentsByModel);
    }

    [GeneratedRegex("\"model\"\\s*:\\s*\"(?<m>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ModelFieldRegex();

    [GeneratedRegex(@"\\?""conversationId\\?""\s*:\s*\\?""(?<id>[A-Za-z0-9\-_]+)\\?""", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnedConversationIdRegex();

    private sealed record AntigravityConversationRecord(
        string Id,
        string Title,
        string Status,
        string LastModifiedRaw);
}
