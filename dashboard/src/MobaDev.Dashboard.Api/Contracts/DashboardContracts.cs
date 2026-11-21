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
    int ActiveSubAgentCount = 0);

public sealed record ActiveAgentsSummary(
    int StandingMonitorsTotal,
    int StandingMonitorsOnline,
    int StandingMonitorsBusy,
    int ActiveSubAgentsTotal,
    int TotalActiveAgents);

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
    int? LinkedPrNumber);

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
