using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Services;

/// <summary>
/// Aggregates all read-only dashboard providers with short TTL caching and card-isolated health metadata.
/// </summary>
public sealed partial class DashboardSnapshotService
{
    private const int ChangelogHeaderScanLineLimit = 200;

    private readonly IOptionsMonitor<DashboardOptions> _optionsMonitor;
    private readonly Providers.ClaudeMonitorProvider _claudeProvider;
    private readonly Providers.CodexMonitorProvider _codexProvider;
    private readonly Providers.AntigravityMonitorProvider _antigravityProvider;
    private readonly Providers.GovernanceProvider _governanceProvider;
    private readonly Providers.ConfigDocsProvider _configDocsProvider;
    private readonly Providers.PipelineProvider _pipelineProvider;
    private readonly Providers.WorktreeProvider _worktreeProvider;
    private readonly Providers.SwarmBusProvider _swarmBusProvider;

    private readonly SemaphoreSlim _localLock = new(1, 1);
    private readonly SemaphoreSlim _pipelineLock = new(1, 1);

    private MonitorsResponse? _cachedMonitors;
    private GovernanceResponse? _cachedGovernance;
    private HygieneResponse? _cachedHygiene;
    private SwarmBusResponse? _cachedSwarmBus;
    private string? _cachedChangelogVersion;
    private DateTimeOffset _localCacheExpiresAt = DateTimeOffset.MinValue;

    private PipelineResponse? _cachedPipeline;
    private DateTimeOffset _pipelineCacheExpiresAt = DateTimeOffset.MinValue;

    public DashboardSnapshotService(
        IOptionsMonitor<DashboardOptions> optionsMonitor,
        Providers.ClaudeMonitorProvider claudeProvider,
        Providers.CodexMonitorProvider codexProvider,
        Providers.AntigravityMonitorProvider antigravityProvider,
        Providers.GovernanceProvider governanceProvider,
        Providers.ConfigDocsProvider configDocsProvider,
        Providers.PipelineProvider pipelineProvider,
        Providers.WorktreeProvider worktreeProvider,
        Providers.SwarmBusProvider swarmBusProvider)
    {
        _optionsMonitor = optionsMonitor;
        _claudeProvider = claudeProvider;
        _codexProvider = codexProvider;
        _antigravityProvider = antigravityProvider;
        _governanceProvider = governanceProvider;
        _configDocsProvider = configDocsProvider;
        _pipelineProvider = pipelineProvider;
        _worktreeProvider = worktreeProvider;
        _swarmBusProvider = swarmBusProvider;
    }

    public async Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var sources = new Dictionary<string, SourceHealth>(StringComparer.OrdinalIgnoreCase)
        {
            ["monitors"] = snapshot.Monitors.Source,
            ["governance"] = snapshot.Governance.Source,
            ["pipeline"] = snapshot.Pipeline.Source,
            ["hygiene"] = snapshot.Hygiene.Source
        };
        if (snapshot.SwarmBus is not null)
        {
            sources["swarmBus"] = snapshot.SwarmBus.Source;
        }

        return new HealthResponse(
            Status: snapshot.OverallStatus,
            DotNetVersion: RuntimeInformation.FrameworkDescription,
            ReadOnly: true,
            BindAddress: snapshot.BindAddress,
            GeneratedAtUtc: snapshot.GeneratedAtUtc,
            Sources: sources);
    }

    public async Task<MonitorsResponse> GetMonitorsAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        return snapshot.Monitors;
    }

    public async Task<GovernanceResponse> GetGovernanceAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        return snapshot.Governance;
    }

    public Task<RuleDiffResponse?> GetRuleDiffAsync(
        string tool,
        string kind,
        string fileName,
        CancellationToken cancellationToken)
    {
        var paths = _optionsMonitor.CurrentValue.ResolvePaths();
        return _governanceProvider.GetRuleDiffAsync(paths, tool, kind, fileName, cancellationToken);
    }

    public Task<ConfigDocsCatalogResponse> GetConfigDocsCatalogAsync(
        string scope,
        CancellationToken cancellationToken)
    {
        var paths = _optionsMonitor.CurrentValue.ResolvePaths();
        return _configDocsProvider.GetCatalogAsync(paths, scope, cancellationToken);
    }

    public Task<ConfigDocContentResponse?> GetConfigDocContentAsync(
        string scope,
        string path,
        CancellationToken cancellationToken)
    {
        var paths = _optionsMonitor.CurrentValue.ResolvePaths();
        return _configDocsProvider.GetDocumentContentAsync(paths, scope, path, cancellationToken);
    }

    public async Task<PipelineResponse> GetPipelineAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        return await GetPipelineCachedAsync(options, forceRefresh, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HygieneResponse> GetHygieneAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        return snapshot.Hygiene;
    }

    public async Task<SwarmBusResponse> GetSwarmBusAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        return snapshot.SwarmBus ?? await _swarmBusProvider.GetSwarmBusAsync(_optionsMonitor.CurrentValue.ResolvePaths(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var paths = options.ResolvePaths();
        var now = DateTimeOffset.UtcNow;

        var pipeline = await GetPipelineCachedAsync(options, forceRefresh, cancellationToken).ConfigureAwait(false);

        MonitorsResponse monitors;
        GovernanceResponse governance;
        HygieneResponse hygiene;
        SwarmBusResponse swarmBus;
        string? changelogVersion;

        await _localLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh &&
                _cachedMonitors is not null &&
                _cachedGovernance is not null &&
                _cachedHygiene is not null &&
                _cachedSwarmBus is not null &&
                now < _localCacheExpiresAt)
            {
                monitors = _cachedMonitors;
                governance = _cachedGovernance;
                hygiene = _cachedHygiene;
                swarmBus = _cachedSwarmBus;
                changelogVersion = _cachedChangelogVersion;
            }
            else
            {
                var busyIdleThreshold = options.ResolveBusyIdleThreshold();
                var claudeTask = _claudeProvider.GetMonitorAsync(paths, busyIdleThreshold, cancellationToken);
                var codexTask = _codexProvider.GetMonitorAsync(paths, cancellationToken);
                var antigravityTask = _antigravityProvider.GetMonitorsAsync(paths, busyIdleThreshold, cancellationToken);
                var governanceTask = _governanceProvider.GetGovernanceAsync(paths, cancellationToken);
                var worktreeTask = _worktreeProvider.GetWorktreesAsync(paths, pipeline.PullRequests, cancellationToken);
                var branchTask = _worktreeProvider.GetBranchHygieneAsync(paths, pipeline.PullRequests, cancellationToken);
                var busTask = _swarmBusProvider.GetSwarmBusAsync(paths, cancellationToken);

                await Task.WhenAll(claudeTask, codexTask, antigravityTask, governanceTask, worktreeTask, branchTask, busTask).ConfigureAwait(false);

                var monitorList = new List<MonitorInfo>(2 + antigravityTask.Result.Count)
                {
                    codexTask.Result,
                    claudeTask.Result
                };
                monitorList.AddRange(antigravityTask.Result);

                var monitorSource = AggregateSourceHealth(monitorList.Select(m => m.Source), now);
                var monitorSummary = ComputeActiveAgentsSummary(monitorList);
                monitors = new MonitorsResponse(now, monitorList, monitorSource, monitorSummary);

                governance = governanceTask.Result;

                var (worktrees, worktreeSource) = worktreeTask.Result;
                var (branches, staleLocalBranches, staleRemoteBranches) = branchTask.Result;
                var sessions = BuildSessionHygieneList(monitorList);
                var staleCount = worktrees.Count(w => w.Status == "stale" && !w.IsDirty);
                hygiene = new HygieneResponse(
                    now,
                    sessions,
                    worktrees,
                    staleCount,
                    worktreeSource,
                    Branches: branches,
                    StaleLocalBranchCount: staleLocalBranches,
                    StaleRemoteBranchCount: staleRemoteBranches);
                swarmBus = busTask.Result;
                changelogVersion = ReadLatestChangelogVersion(paths.MobaDevRepoPath);

                _cachedMonitors = monitors;
                _cachedGovernance = governance;
                _cachedHygiene = hygiene;
                _cachedSwarmBus = swarmBus;
                _cachedChangelogVersion = changelogVersion;

                var ttl = Math.Max(1, options.LocalCacheTtlSeconds);
                _localCacheExpiresAt = now.AddSeconds(ttl);
            }
        }
        finally
        {
            _localLock.Release();
        }

        var (overallStatus, warningCount) = ComputeOverallStatus(monitors, governance, pipeline, hygiene);

        return new DashboardSnapshot(
            GeneratedAtUtc: now,
            ReadOnly: true,
            BindAddress: options.BindUrl,
            OverallStatus: overallStatus,
            WarningCount: warningCount,
            Monitors: monitors,
            Governance: governance,
            Pipeline: pipeline,
            Hygiene: hygiene,
            ChangelogVersion: changelogVersion,
            SwarmBus: swarmBus);
    }

    private async Task<PipelineResponse> GetPipelineCachedAsync(
        DashboardOptions options,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await _pipelineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cachedPipeline is not null && now < _pipelineCacheExpiresAt)
            {
                return _cachedPipeline;
            }

            var result = await _pipelineProvider.GetPipelineAsync(options, cancellationToken).ConfigureAwait(false);
            _cachedPipeline = result;

            var ttl = Math.Max(5, options.GitHubCacheTtlSeconds);
            _pipelineCacheExpiresAt = now.AddSeconds(ttl);
            return result;
        }
        finally
        {
            _pipelineLock.Release();
        }
    }

    private static ActiveAgentsSummary ComputeActiveAgentsSummary(IReadOnlyList<MonitorInfo> monitors)
    {
        var standingTotal = monitors.Count;
        var standingOnline = monitors.Count(m => m.State is "standby" or "idle" or "busy" or "rate_limited");
        var standingBusy = monitors.Count(m => m.State == "busy");
        var activeSubAgents = monitors.Sum(m => m.ActiveSubAgentCount);
        var subAgentsByModel = SubAgentModelCatalog.BuildSummaryList(monitors);
        return new ActiveAgentsSummary(
            StandingMonitorsTotal: standingTotal,
            StandingMonitorsOnline: standingOnline,
            StandingMonitorsBusy: standingBusy,
            ActiveSubAgentsTotal: activeSubAgents,
            TotalActiveAgents: standingBusy + activeSubAgents,
            SubAgentsByModel: subAgentsByModel);
    }

    private static List<ToolSessionHygiene> BuildSessionHygieneList(IReadOnlyList<MonitorInfo> monitors)
    {
        var results = new List<ToolSessionHygiene>();
        foreach (var group in monitors.GroupBy(m => m.Tool, StringComparer.OrdinalIgnoreCase))
        {
            var toolMonitors = group.ToList();
            var targetCount = toolMonitors.Count;
            var actualCount = toolMonitors.Sum(m => m.ActiveSessionCount);
            var monitorName = toolMonitors.Count == 1
                ? toolMonitors[0].DisplayName
                : string.Join(" + ", toolMonitors.Select(m => m.DisplayName));

            string status;
            string? notice;
            if (actualCount == 0)
            {
                status = "inactive";
                notice = targetCount > 1
                    ? "No standing Root Head sessions detected."
                    : "No standing monitor session detected.";
            }
            else if (actualCount <= targetCount)
            {
                status = "ok";
                notice = null;
            }
            else
            {
                status = "warning";
                notice = $"{actualCount} active sessions found (target: {targetCount}) — archive extra sessions.";
            }

            results.Add(new ToolSessionHygiene(
                Tool: group.Key,
                MonitorName: monitorName,
                TargetCount: targetCount,
                ActualCount: actualCount,
                Status: status,
                Notice: notice));
        }

        return results;
    }

    private static SourceHealth AggregateSourceHealth(IEnumerable<SourceHealth> items, DateTimeOffset now)
    {
        var list = items.ToList();
        if (list.All(s => s.Status == "unavailable"))
        {
            return new SourceHealth("unavailable", now, "No local monitor sources available.");
        }

        if (list.Any(s => s.Status is "degraded" or "unavailable"))
        {
            var degradedCount = list.Count(s => s.Status != "ok");
            return new SourceHealth("degraded", now, $"{degradedCount} of {list.Count} monitor sources degraded.");
        }

        return new SourceHealth("ok", now);
    }

    private static (string OverallStatus, int WarningCount) ComputeOverallStatus(
        MonitorsResponse monitors,
        GovernanceResponse governance,
        PipelineResponse pipeline,
        HygieneResponse hygiene)
    {
        var warnings = 0;

        warnings += monitors.Monitors.Count(m => m.State == "rate_limited");
        warnings += hygiene.Sessions.Count(s => s.Status == "warning");
        warnings += governance.DriftCount + governance.MissingCount;
        warnings += hygiene.StaleWorktreeCount;
        warnings += hygiene.StaleLocalBranchCount + hygiene.StaleRemoteBranchCount;

        var anyDegraded = new[]
        {
            monitors.Source.Status,
            governance.Source.Status,
            pipeline.Source.Status,
            hygiene.Source.Status
        }.Any(s => s is "degraded" or "unavailable");

        if (anyDegraded)
        {
            return ("degraded", warnings);
        }

        return warnings > 0 ? ("warnings", warnings) : ("healthy", 0);
    }

    private static string? ReadLatestChangelogVersion(string mobaDevRepoPath)
    {
        if (string.IsNullOrWhiteSpace(mobaDevRepoPath))
        {
            return null;
        }

        var changelogPath = Path.Combine(mobaDevRepoPath, "CHANGELOG.md");
        if (!File.Exists(changelogPath) || !Security.PathWhitelistGuard.IsAllowedFilePath(changelogPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(changelogPath).Take(ChangelogHeaderScanLineLimit))
            {
                var match = ChangelogVersionHeadingRegex().Match(line);
                if (match.Success)
                {
                    return match.Groups["ver"].Value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    [GeneratedRegex(@"^##\s+\[(?<ver>[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.\-]+)?)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ChangelogVersionHeadingRegex();
}
