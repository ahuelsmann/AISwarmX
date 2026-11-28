using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;

namespace AISwarmX.Dashboard.Api.Services;

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

    private sealed record CachedHealthEntry(
        string WorkspaceKey,
        HealthResponse Response,
        DateTimeOffset ExpiresAtUtc);

    private static readonly TimeSpan DefaultColdHealthWaitBudget = TimeSpan.FromMilliseconds(1500);

    private readonly SemaphoreSlim _localLock = new(1, 1);
    private readonly SemaphoreSlim _pipelineLock = new(1, 1);
    private readonly Lock _healthRefreshGate = new();

    private (string WorkspaceKey, Task RefreshTask)? _inflightHealthRefresh;
    private CachedHealthEntry? _cachedHealthEntry;

    private MonitorsResponse? _cachedMonitors;
    private GovernanceResponse? _cachedGovernance;
    private HygieneResponse? _cachedHygiene;
    private SwarmBusResponse? _cachedSwarmBus;
    private string? _cachedChangelogVersion;
    private string? _cachedLocalKey;
    private long _swarmBusCacheEpoch;
    private DateTimeOffset _localCacheExpiresAt = DateTimeOffset.MinValue;

    private PipelineResponse? _cachedPipeline;
    private string? _cachedPipelineKey;
    private DateTimeOffset _pipelineCacheExpiresAt = DateTimeOffset.MinValue;

    internal TimeSpan ColdHealthWaitBudget { get; set; } = DefaultColdHealthWaitBudget;

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
        cancellationToken.ThrowIfCancellationRequested();

        var options = _optionsMonitor.CurrentValue;
        var paths = options.ResolvePaths();
        var workspaceKey = GetCurrentHealthWorkspaceKey(options, paths);

        var cached = Volatile.Read(ref _cachedHealthEntry);
        if (cached is not null &&
            string.Equals(cached.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase))
        {
            if (DateTimeOffset.UtcNow >= cached.ExpiresAtUtc)
            {
                _ = EnsureBackgroundSnapshotRefreshStarted(workspaceKey);
            }

            return cached.Response;
        }

        var refreshTask = EnsureBackgroundSnapshotRefreshStarted(workspaceKey);
        try
        {
            await refreshTask.WaitAsync(ColdHealthWaitBudget, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Cold-start or workspace-switch wait budget elapsed; return "starting" while background refresh continues.
        }

        cached = Volatile.Read(ref _cachedHealthEntry);
        if (cached is not null &&
            string.Equals(cached.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase))
        {
            return cached.Response;
        }

        return new HealthResponse(
            Status: "starting",
            DotNetVersion: RuntimeInformation.FrameworkDescription,
            ReadOnly: true,
            BindAddress: options.BindUrl,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Sources: new Dictionary<string, SourceHealth>(StringComparer.OrdinalIgnoreCase));
    }

    internal void ExpireCachesForTesting()
    {
        _pipelineCacheExpiresAt = DateTimeOffset.MinValue;
        _localCacheExpiresAt = DateTimeOffset.MinValue;
        var existing = Volatile.Read(ref _cachedHealthEntry);
        if (existing is not null)
        {
            Volatile.Write(ref _cachedHealthEntry, existing with { ExpiresAtUtc = DateTimeOffset.MinValue });
        }
    }

    internal Task WaitForInflightHealthRefreshForTestingAsync()
    {
        lock (_healthRefreshGate)
        {
            return _inflightHealthRefresh?.RefreshTask ?? Task.CompletedTask;
        }
    }

    private Task EnsureBackgroundSnapshotRefreshStarted(string workspaceKey)
    {
        lock (_healthRefreshGate)
        {
            if (_inflightHealthRefresh is { } inflight &&
                !inflight.RefreshTask.IsCompleted &&
                string.Equals(inflight.WorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase))
            {
                return inflight.RefreshTask;
            }

            var refreshTask = Task.Run(async () =>
            {
                try
                {
                    await GetSnapshotAsync(forceRefresh: false, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Background refresh errors never fault health callers.
                }
            });

            _inflightHealthRefresh = (workspaceKey, refreshTask);
            return refreshTask;
        }
    }

    private static HealthResponse BuildHealthResponseFromSnapshot(DashboardSnapshot snapshot)
    {
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

    private static string GetCurrentHealthWorkspaceKey(DashboardOptions options, ResolvedDashboardPaths paths) =>
        $"{options.BindUrl?.Trim()}||{BuildPipelineCacheKey(options)}||{BuildLocalCacheKey(paths, options)}";

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
        var (pipeline, _) = await GetPipelineCachedAsync(options, forceRefresh, cancellationToken).ConfigureAwait(false);
        return pipeline;
    }

    public async Task<HygieneResponse> GetHygieneAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        return snapshot.Hygiene;
    }

    public async Task<SwarmBusResponse> GetSwarmBusAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (forceRefresh)
        {
            return await RefreshSwarmBusAsync(cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await GetSnapshotAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        return snapshot.SwarmBus ?? await RefreshSwarmBusAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<SwarmBusResponse> RefreshSwarmBusAsync(CancellationToken cancellationToken) =>
        RefreshSwarmBusAsync(maxAttempts: DefaultMaxRefreshAttempts, cancellationToken);

    internal async Task<SwarmBusResponse> RefreshSwarmBusAsync(int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var refreshed = await TryRefreshActiveSwarmBusAsync(cancellationToken).ConfigureAwait(false);
            if (refreshed is not null)
            {
                return refreshed;
            }
        }

        throw new SwarmBusContentionException(
            $"Swarm bus refresh superseded after {maxAttempts} attempts; retry request.");
    }

    private const int DefaultMaxRefreshAttempts = 8;

    public async Task<SwarmBusResponse?> TryRefreshActiveSwarmBusAsync(CancellationToken cancellationToken)
    {
        DashboardOptions options;
        ResolvedDashboardPaths paths;
        string localCacheKey;
        string observedWorkspaceKey;
        long observedEpoch;

        await _localLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            options = _optionsMonitor.CurrentValue;
            paths = options.ResolvePaths();
            localCacheKey = BuildLocalCacheKey(paths, options);
            observedWorkspaceKey = GetCurrentHealthWorkspaceKey(options, paths);
            observedEpoch = _swarmBusCacheEpoch;
        }
        finally
        {
            _localLock.Release();
        }

        var fresh = await _swarmBusProvider.GetSwarmBusAsync(paths, cancellationToken).ConfigureAwait(false);

        await _localLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentOptions = _optionsMonitor.CurrentValue;
            var currentPaths = currentOptions.ResolvePaths();
            var currentKey = BuildLocalCacheKey(currentPaths, currentOptions);
            var currentWorkspaceKey = GetCurrentHealthWorkspaceKey(currentOptions, currentPaths);
            var sameWorkspace = string.Equals(currentKey, localCacheKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentWorkspaceKey, observedWorkspaceKey, StringComparison.OrdinalIgnoreCase)
                && (_cachedLocalKey is null || string.Equals(_cachedLocalKey, localCacheKey, StringComparison.OrdinalIgnoreCase));
            if (sameWorkspace && observedEpoch == _swarmBusCacheEpoch)
            {
                _cachedSwarmBus = fresh;
                _cachedLocalKey ??= localCacheKey;
                _swarmBusCacheEpoch++;

                var existingHealth = Volatile.Read(ref _cachedHealthEntry);
                if (existingHealth is not null &&
                    string.Equals(existingHealth.WorkspaceKey, currentWorkspaceKey, StringComparison.OrdinalIgnoreCase))
                {
                    var updatedSources = new Dictionary<string, SourceHealth>(
                        existingHealth.Response.Sources,
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["swarmBus"] = fresh.Source
                    };
                    Volatile.Write(
                        ref _cachedHealthEntry,
                        existingHealth with { Response = existingHealth.Response with { Sources = updatedSources } });
                }

                return fresh;
            }

            return null;
        }
        finally
        {
            _localLock.Release();
        }
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var paths = options.ResolvePaths();
        var workspaceKey = GetCurrentHealthWorkspaceKey(options, paths);
        var now = DateTimeOffset.UtcNow;

        var (pipeline, pipelineExpiresAt) = await GetPipelineCachedAsync(options, forceRefresh, cancellationToken).ConfigureAwait(false);

        MonitorsResponse monitors;
        GovernanceResponse governance;
        HygieneResponse hygiene;
        SwarmBusResponse swarmBus;
        string? changelogVersion;
        DateTimeOffset localExpiresAt;

        var localCacheKey = BuildLocalCacheKey(paths, options);
        await _localLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh &&
                string.Equals(_cachedLocalKey, localCacheKey, StringComparison.OrdinalIgnoreCase) &&
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
                localExpiresAt = _localCacheExpiresAt;
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
                _cachedLocalKey = localCacheKey;
                _swarmBusCacheEpoch++;

                var ttl = Math.Max(1, options.LocalCacheTtlSeconds);
                localExpiresAt = now.AddSeconds(ttl);
                _localCacheExpiresAt = localExpiresAt;
            }
        }
        finally
        {
            _localLock.Release();
        }

        var (overallStatus, warningCount) = ComputeOverallStatus(monitors, governance, pipeline, hygiene);

        var snapshot = new DashboardSnapshot(
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

        var currentOptions = _optionsMonitor.CurrentValue;
        var currentWorkspaceKey = GetCurrentHealthWorkspaceKey(currentOptions, currentOptions.ResolvePaths());
        if (string.Equals(currentWorkspaceKey, workspaceKey, StringComparison.OrdinalIgnoreCase))
        {
            var healthExpiresAt = pipelineExpiresAt < localExpiresAt ? pipelineExpiresAt : localExpiresAt;
            Volatile.Write(
                ref _cachedHealthEntry,
                new CachedHealthEntry(workspaceKey, BuildHealthResponseFromSnapshot(snapshot), healthExpiresAt));
        }

        return snapshot;
    }

    private async Task<(PipelineResponse Response, DateTimeOffset ExpiresAtUtc)> GetPipelineCachedAsync(
        DashboardOptions options,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pipelineKey = BuildPipelineCacheKey(options);

        await _pipelineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh &&
                _cachedPipeline is not null &&
                string.Equals(_cachedPipelineKey, pipelineKey, StringComparison.OrdinalIgnoreCase) &&
                now < _pipelineCacheExpiresAt)
            {
                return (_cachedPipeline, _pipelineCacheExpiresAt);
            }

            var result = await _pipelineProvider.GetPipelineAsync(options, cancellationToken).ConfigureAwait(false);
            _cachedPipeline = result;
            _cachedPipelineKey = pipelineKey;

            var ttl = Math.Max(5, options.GitHubCacheTtlSeconds);
            var expiresAt = now.AddSeconds(ttl);
            _pipelineCacheExpiresAt = expiresAt;
            return (result, expiresAt);
        }
        finally
        {
            _pipelineLock.Release();
        }
    }

    private static string BuildPipelineCacheKey(DashboardOptions options) =>
        $"{options.GitHubRepository?.Trim()}|{options.GitHubProjectOwner?.Trim()}|{options.GitHubProjectNumber}";

    private static string BuildLocalCacheKey(ResolvedDashboardPaths paths, DashboardOptions options) =>
        $"{paths.MobaDevRepoPath}|{paths.MobaFlowRepoPath}|{paths.SwarmBusDbPath}|{options.BusyIdleAfterMinutes}";

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

/// <summary>
/// Raised when bounded <see cref="DashboardSnapshotService.RefreshSwarmBusAsync(CancellationToken)"/> attempts
/// are repeatedly superseded by concurrent workspace changes or cache updates.
/// </summary>
public sealed class SwarmBusContentionException(string message) : Exception(message);
