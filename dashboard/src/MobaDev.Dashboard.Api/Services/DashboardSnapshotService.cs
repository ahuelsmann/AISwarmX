using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Services;

/// <summary>
/// Aggregates all read-only dashboard providers with short TTL caching and card-isolated health metadata.
/// </summary>
public sealed class DashboardSnapshotService
{
    private readonly IOptionsMonitor<DashboardOptions> _optionsMonitor;
    private readonly Providers.ClaudeMonitorProvider _claudeProvider;
    private readonly Providers.CodexMonitorProvider _codexProvider;
    private readonly Providers.AntigravityMonitorProvider _antigravityProvider;
    private readonly Providers.GovernanceProvider _governanceProvider;
    private readonly Providers.ConfigDocsProvider _configDocsProvider;
    private readonly Providers.PipelineProvider _pipelineProvider;
    private readonly Providers.WorktreeProvider _worktreeProvider;

    private readonly SemaphoreSlim _localLock = new(1, 1);
    private readonly SemaphoreSlim _pipelineLock = new(1, 1);

    private MonitorsResponse? _cachedMonitors;
    private GovernanceResponse? _cachedGovernance;
    private HygieneResponse? _cachedHygiene;
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
        Providers.WorktreeProvider worktreeProvider)
    {
        _optionsMonitor = optionsMonitor;
        _claudeProvider = claudeProvider;
        _codexProvider = codexProvider;
        _antigravityProvider = antigravityProvider;
        _governanceProvider = governanceProvider;
        _configDocsProvider = configDocsProvider;
        _pipelineProvider = pipelineProvider;
        _worktreeProvider = worktreeProvider;
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

    public async Task<DashboardSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var paths = options.ResolvePaths();
        var now = DateTimeOffset.UtcNow;

        var pipeline = await GetPipelineCachedAsync(options, forceRefresh, cancellationToken).ConfigureAwait(false);

        MonitorsResponse monitors;
        GovernanceResponse governance;
        HygieneResponse hygiene;

        await _localLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh &&
                _cachedMonitors is not null &&
                _cachedGovernance is not null &&
                _cachedHygiene is not null &&
                now < _localCacheExpiresAt)
            {
                monitors = _cachedMonitors;
                governance = _cachedGovernance;
                hygiene = _cachedHygiene;
            }
            else
            {
                var claudeTask = _claudeProvider.GetMonitorAsync(paths, cancellationToken);
                var codexTask = _codexProvider.GetMonitorAsync(paths, cancellationToken);
                var antigravityTask = _antigravityProvider.GetMonitorAsync(paths, cancellationToken);
                var governanceTask = _governanceProvider.GetGovernanceAsync(paths, cancellationToken);
                var worktreeTask = _worktreeProvider.GetWorktreesAsync(paths, pipeline.PullRequests, cancellationToken);

                await Task.WhenAll(claudeTask, codexTask, antigravityTask, governanceTask, worktreeTask).ConfigureAwait(false);

                var monitorList = new[] { codexTask.Result, claudeTask.Result, antigravityTask.Result };
                var monitorSource = AggregateSourceHealth(monitorList.Select(m => m.Source), now);
                monitors = new MonitorsResponse(now, monitorList, monitorSource);

                governance = governanceTask.Result;

                var (worktrees, worktreeSource) = worktreeTask.Result;
                var sessions = monitorList.Select(m => BuildSessionHygiene(m)).ToList();
                var staleCount = worktrees.Count(w => w.Status == "stale");
                hygiene = new HygieneResponse(now, sessions, worktrees, staleCount, worktreeSource);

                _cachedMonitors = monitors;
                _cachedGovernance = governance;
                _cachedHygiene = hygiene;

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
            Hygiene: hygiene);
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

    private static ToolSessionHygiene BuildSessionHygiene(MonitorInfo monitor)
    {
        var status = monitor.ActiveSessionCount switch
        {
            1 => "ok",
            0 => "inactive",
            _ => "warning"
        };

        var notice = monitor.ActiveSessionCount > 1
            ? $"{monitor.ActiveSessionCount} active sessions found — archive extra sessions."
            : monitor.ActiveSessionCount == 0
                ? "No standing monitor session detected."
                : null;

        return new ToolSessionHygiene(
            Tool: monitor.Tool,
            MonitorName: monitor.DisplayName,
            TargetCount: 1,
            ActualCount: monitor.ActiveSessionCount,
            Status: status,
            Notice: notice);
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
}
