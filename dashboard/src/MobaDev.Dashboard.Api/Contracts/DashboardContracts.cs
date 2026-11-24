namespace MobaDev.Dashboard.Api.Contracts;

/// <summary>
/// Uniform per-source health metadata enabling card-isolated graceful degradation.
/// </summary>
public sealed record SourceHealth(
    string Status,
    DateTimeOffset UpdatedAtUtc,
    string? Notice = null,
    bool FromCache = false);

public sealed record HealthResponse(
    string Status,
    string DotNetVersion,
    bool ReadOnly,
    string BindAddress,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, SourceHealth> Sources);

public sealed record MonitorInfo(
    string Id,
    string DisplayName,
    string Role,
    string Profile,
    string Tool,
    string State,
    string? Model,
    DateTimeOffset? LastActivityUtc,
    string? RateLimitResetLabel,
    int ActiveSessionCount,
    string? AutomationStatus,
    SourceHealth Source,
    int ActiveSubAgentCount = 0,
    IReadOnlyDictionary<string, int>? ActiveSubAgentsByModel = null);

public sealed record SubAgentModelCount(
    string Model,
    string Profile,
    int Count);

public sealed record ActiveAgentsSummary(
    int StandingMonitorsTotal,
    int StandingMonitorsOnline,
    int StandingMonitorsBusy,
    int ActiveSubAgentsTotal,
    int TotalActiveAgents,
    IReadOnlyList<SubAgentModelCount>? SubAgentsByModel = null);

public static class SubAgentModelCatalog
{
    public static readonly IReadOnlyList<(string Model, string Profile)> CanonicalModels =
    [
        ("GPT-6.2-sol", "P2"),
        ("GPT-6.2-astra", "P2"),
        ("Claude Opus 5.5", "P1"),
        ("Gemini 4 Carbon", "P3"),
        ("Gemini 4 Hydrogen", "P4"),
        ("Claude Sonnet 5.5", "P4"),
        ("Claude Haiku 4.5", "P4"),
        ("GPT-OSS 120B", "P2")
    ];

    public static (string Model, string Profile) Normalize(string? rawModel, string fallbackModel = "Gemini 4 Carbon")
    {
        var trimmed = rawModel?.Trim() ?? string.Empty;
        var candidate = string.IsNullOrWhiteSpace(trimmed) ||
                        string.Equals(trimmed, "codex-auto-review", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmed, "<synthetic>", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(trimmed, "synthetic", StringComparison.OrdinalIgnoreCase)
            ? fallbackModel
            : trimmed;

        var lower = candidate.ToLowerInvariant();
        if (lower.Contains("opus", StringComparison.Ordinal))
        {
            return ("Claude Opus 5.5", "P1");
        }

        if (lower.Contains("sonnet", StringComparison.Ordinal) || lower.Contains("fable", StringComparison.Ordinal))
        {
            return ("Claude Sonnet 5.5", "P4");
        }

        if (lower.Contains("haiku", StringComparison.Ordinal))
        {
            return ("Claude Haiku 4.5", "P4");
        }

        if (lower.Contains("claude", StringComparison.Ordinal))
        {
            return ("Claude Opus 5.5", "P1");
        }

        if (lower.Contains("hydrogen", StringComparison.Ordinal))
        {
            return ("Gemini 4 Hydrogen", "P4");
        }

        if (lower.Contains("argon", StringComparison.Ordinal) ||
            lower.Contains("carbon", StringComparison.Ordinal) ||
            lower.Contains("neon", StringComparison.Ordinal) ||
            lower.Contains("gemini", StringComparison.Ordinal) ||
            lower.Contains("antigravity", StringComparison.Ordinal))
        {
            return ("Gemini 4 Carbon", "P3");
        }

        if (lower.Contains("gpt-oss", StringComparison.Ordinal) ||
            lower.Contains("120b", StringComparison.Ordinal))
        {
            return ("GPT-OSS 120B", "P2");
        }

        if (lower.Contains("astra", StringComparison.Ordinal))
        {
            return ("GPT-6.2-astra", "P2");
        }

        if (lower.Contains("gpt", StringComparison.Ordinal) ||
            lower.Contains("sol", StringComparison.Ordinal) ||
            lower.Contains("codex", StringComparison.Ordinal))
        {
            return ("GPT-6.2-sol", "P2");
        }

        return (candidate, "P3");
    }

    public static bool IsClaudeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var lower = model.ToLowerInvariant();
        return lower.Contains("claude", StringComparison.Ordinal) ||
               lower.Contains("opus", StringComparison.Ordinal) ||
               lower.Contains("sonnet", StringComparison.Ordinal) ||
               lower.Contains("haiku", StringComparison.Ordinal);
    }

    public static IReadOnlyList<SubAgentModelCount> BuildSummaryList(IReadOnlyList<MonitorInfo> monitors)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonicalModel, profile) in CanonicalModels)
        {
            counts[canonicalModel] = 0;
            profiles[canonicalModel] = profile;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.ActiveSubAgentsByModel is { Count: > 0 } byModel)
            {
                foreach (var kvp in byModel)
                {
                    if (kvp.Value <= 0)
                    {
                        continue;
                    }

                    var (normModel, normProfile) = Normalize(kvp.Key, monitor.Model ?? "Gemini 4 Carbon");
                    counts[normModel] = counts.GetValueOrDefault(normModel) + kvp.Value;
                    profiles.TryAdd(normModel, normProfile);
                }
            }
            else if (monitor.ActiveSubAgentCount > 0)
            {
                var fallback = monitor.Tool.Contains("Codex", StringComparison.OrdinalIgnoreCase)
                    ? "GPT-6.2-sol"
                    : monitor.Tool.Contains("Claude", StringComparison.OrdinalIgnoreCase)
                        ? "Claude Opus 5.5"
                        : "Gemini 4 Carbon";
                var (normModel, normProfile) = Normalize(monitor.Model, fallback);
                counts[normModel] = counts.GetValueOrDefault(normModel) + monitor.ActiveSubAgentCount;
                profiles.TryAdd(normModel, normProfile);
            }
        }

        var result = new List<SubAgentModelCount>(counts.Count);
        foreach (var (canonicalModel, profile) in CanonicalModels)
        {
            result.Add(new SubAgentModelCount(canonicalModel, profile, counts.GetValueOrDefault(canonicalModel)));
        }

        foreach (var kvp in counts)
        {
            if (CanonicalModels.Any(c => string.Equals(c.Model, kvp.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new SubAgentModelCount(kvp.Key, profiles.GetValueOrDefault(kvp.Key, "P3"), kvp.Value));
        }

        return result;
    }
}

public sealed record MonitorsResponse(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<MonitorInfo> Monitors,
    SourceHealth Source,
    ActiveAgentsSummary? Summary = null);

public sealed record CapabilityProfileInfo(
    string Profile,
    string Name,
    string Models,
    string Strengths);

public sealed record RoleDefinitionInfo(
    string Role,
    string Profiles,
    string Responsibility);

public sealed record RiskClassInfo(
    string Code,
    string Name,
    string Areas,
    string RequiredDiversity);

public sealed record AgentSyncItem(
    string Tool,
    string Kind,
    string Name,
    string FileName,
    string? Description,
    string? ProfileHint,
    string SyncStatus,
    string? RepoHash,
    string? LocalHash,
    string? DriftSummary);

public sealed record SkillInfo(
    string Name,
    string Description,
    string SourceRepo,
    string RelativePath);

public sealed record GovernanceResponse(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CapabilityProfileInfo> Profiles,
    IReadOnlyList<RoleDefinitionInfo> Roles,
    IReadOnlyList<RiskClassInfo> RiskClasses,
    IReadOnlyList<AgentSyncItem> Agents,
    IReadOnlyList<SkillInfo> Skills,
    int SyncedCount,
    int DriftCount,
    int MissingCount,
    SourceHealth Source);

public sealed record DiffLineInfo(
    string Kind,
    int? TargetLineNumber,
    int? ActualLineNumber,
    string Text);

public sealed record RuleDiffResponse(
    string Tool,
    string Kind,
    string FileName,
    string SyncStatus,
    string? TargetHash,
    string? ActualHash,
    int AddedLines,
    int RemovedLines,
    bool Truncated,
    IReadOnlyList<DiffLineInfo> Lines);

public sealed record ConfigDocItem(
    string Scope,
    string RelativePath,
    string FileName,
    string Group,
    string Repository,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

public sealed record ConfigDocsCatalogResponse(
    DateTimeOffset GeneratedAtUtc,
    string Scope,
    string Repository,
    IReadOnlyList<ConfigDocItem> Files,
    SourceHealth Source);

public sealed record ConfigDocContentResponse(
    string Scope,
    string RelativePath,
    string FileName,
    string Group,
    string Repository,
    long SizeBytes,
    int LineCount,
    DateTimeOffset LastModifiedUtc,
    bool Truncated,
    string Content);

public sealed record TaskParticipantInfo(
    string Agent,
    string Tool,
    string Role,
    string Profile,
    string Model,
    string Status,
    string EvidenceSource);

public sealed record LinkedIssueInfo(
    int Number,
    string? Title,
    string Url,
    string? State);

public sealed record LinkedPullRequestRef(
    int Number,
    string Title,
    string Url,
    string StateLabel,
    bool IsMerged);

public sealed record ActiveIssueInfo(
    int Number,
    string ItemKind,
    string Title,
    string Url,
    string GitHubState,
    string KanbanStatus,
    string? Priority,
    string? Area,
    string? IssueType,
    string? RiskClass,
    string? Writer,
    IReadOnlyList<string> LockedAreas,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<LinkedPullRequestRef> LinkedPullRequests,
    IReadOnlyList<LinkedIssueInfo> LinkedIssues,
    IReadOnlyList<TaskParticipantInfo> Participants,
    string? BoardDriftReason);

public sealed record PullRequestInfo(
    int Number,
    string Title,
    string Url,
    string Branch,
    string State,
    string StateLabel,
    bool IsDraft,
    bool AutoMergeEnabled,
    string MergeState,
    string CiStatus,
    string SonarStatus,
    string RiskClass,
    IReadOnlyList<string> LockedAreas,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? MergedAtUtc,
    string? ProjectStatus,
    IReadOnlyList<TaskParticipantInfo> Participants,
    IReadOnlyList<LinkedIssueInfo> LinkedIssues,
    IReadOnlyList<string> MergeBlockReasons);

public sealed record PipelineResponse(
    DateTimeOffset GeneratedAtUtc,
    string Repository,
    IReadOnlyList<PullRequestInfo> PullRequests,
    IReadOnlyList<PullRequestInfo> RecentlyMerged,
    IReadOnlyList<ActiveIssueInfo> ActiveIssues,
    bool ProjectBoardAvailable,
    string? ProjectBoardNotice,
    string? ProjectBoardUrl,
    SourceHealth Source);

public sealed record ToolSessionHygiene(
    string Tool,
    string MonitorName,
    int TargetCount,
    int ActualCount,
    string Status,
    string? Notice);

public sealed record WorktreeInfo(
    string FolderName,
    string Branch,
    string HeadShort,
    bool IsMain,
    string Status,
    int? LinkedPrNumber,
    bool IsDirty = false);

public sealed record BranchHygieneInfo(
    string Repository,
    string Name,
    string Scope,
    string Status,
    bool UpstreamGone,
    int? LinkedPrNumber);

public sealed record HygieneResponse(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ToolSessionHygiene> Sessions,
    IReadOnlyList<WorktreeInfo> Worktrees,
    int StaleWorktreeCount,
    SourceHealth Source,
    IReadOnlyList<BranchHygieneInfo>? Branches = null,
    int StaleLocalBranchCount = 0,
    int StaleRemoteBranchCount = 0);

public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAtUtc,
    bool ReadOnly,
    string BindAddress,
    string OverallStatus,
    int WarningCount,
    MonitorsResponse Monitors,
    GovernanceResponse Governance,
    PipelineResponse Pipeline,
    HygieneResponse Hygiene,
    string? ChangelogVersion = null);
