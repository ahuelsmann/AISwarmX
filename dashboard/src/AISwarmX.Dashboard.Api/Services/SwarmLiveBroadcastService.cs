using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;
using AISwarmX.Dashboard.Api.Hubs;
using AISwarmX.SwarmBus.Core.Contracts;

namespace AISwarmX.Dashboard.Api.Services;

/// <summary>
/// Watches the active Swarm Bus SQLite WAL directory and pushes debounced updates over <see cref="SwarmDashboardHub"/>.
/// Also broadcasts periodic snapshot refreshes so main landing / PR news-ticker updates arrive without client polling.
/// </summary>
public sealed class SwarmLiveBroadcastService(
    DashboardSnapshotService snapshotService,
    IHubContext<SwarmDashboardHub, ISwarmDashboardClient> hubContext,
    IOptionsMonitor<DashboardOptions> optionsMonitor,
    ILogger<SwarmLiveBroadcastService> logger) : BackgroundService
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WatcherRetryInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var busChangedSignal = new SemaphoreSlim(0, 1);
        var watcherGate = new Lock();
        FileSystemWatcher? watcher = null;
        string? watchedPath = null;
        var stopping = false;

        void TrySignalChange()
        {
            try
            {
                if (busChangedSignal.CurrentCount == 0)
                {
                    busChangedSignal.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Shutdown in progress.
            }
            catch (SemaphoreFullException)
            {
                // Change already queued.
            }
        }

        void EnsureWatcherConfigured()
        {
            var configuredPath = optionsMonitor.CurrentValue.ResolvePaths().SwarmBusDbPath;
            var normalizedDir = string.IsNullOrWhiteSpace(configuredPath) ? null : Path.GetDirectoryName(configuredPath);
            var fileName = string.IsNullOrWhiteSpace(configuredPath) ? null : Path.GetFileName(configuredPath);
            var targetKey = string.IsNullOrWhiteSpace(normalizedDir) || string.IsNullOrWhiteSpace(fileName)
                ? null
                : Path.Combine(normalizedDir, fileName);

            lock (watcherGate)
            {
                if (stopping ||
                    (watcher is not null && string.Equals(watchedPath, targetKey, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                watcher?.Dispose();
                watcher = null;
                watchedPath = targetKey;

                if (string.IsNullOrWhiteSpace(normalizedDir) ||
                    string.IsNullOrWhiteSpace(fileName) ||
                    !Directory.Exists(normalizedDir))
                {
                    return;
                }

                try
                {
                    var created = new FileSystemWatcher(normalizedDir, $"{fileName}*")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        IncludeSubdirectories = false
                    };

                    static bool ShouldIgnoreEvent(string? name) =>
                        !string.IsNullOrEmpty(name) && name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase);

                    created.Changed += (_, e) =>
                    {
                        if (!ShouldIgnoreEvent(e.Name)) TrySignalChange();
                    };
                    created.Created += (_, e) =>
                    {
                        if (!ShouldIgnoreEvent(e.Name)) TrySignalChange();
                    };
                    created.Renamed += (_, e) =>
                    {
                        if (!ShouldIgnoreEvent(e.Name)) TrySignalChange();
                    };
                    created.Error += (_, _) =>
                    {
                        lock (watcherGate)
                        {
                            if (ReferenceEquals(watcher, created))
                            {
                                watcher?.Dispose();
                                watcher = null;
                            }
                        }

                        TrySignalChange();
                    };
                    created.EnableRaisingEvents = true;
                    watcher = created;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    logger.LogDebug(ex, "SwarmBus watcher setup deferred for directory {Directory}.", normalizedDir);
                    watcher?.Dispose();
                    watcher = null;
                }
            }
        }

        using var optionsSubscription = optionsMonitor.OnChange(_ => EnsureWatcherConfigured());
        EnsureWatcherConfigured();

        var lastTickerKey = string.Empty;
        var nextSnapshotCheckUtc = DateTimeOffset.UtcNow.AddSeconds(GetSnapshotIntervalSeconds());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                EnsureWatcherConfigured();

                var signaled = await busChangedSignal.WaitAsync(WatcherRetryInterval, stoppingToken).ConfigureAwait(false);
                if (signaled)
                {
                    // Coalesce burst writes across WAL + main db files.
                    await Task.Delay(DebounceDelay, stoppingToken).ConfigureAwait(false);
                    while (busChangedSignal.CurrentCount > 0)
                    {
                        await busChangedSignal.WaitAsync(0, stoppingToken).ConfigureAwait(false);
                    }

                    await PublishSwarmBusUpdateAsync(stoppingToken).ConfigureAwait(false);
                }

                var now = DateTimeOffset.UtcNow;
                if (now >= nextSnapshotCheckUtc)
                {
                    nextSnapshotCheckUtc = now.AddSeconds(GetSnapshotIntervalSeconds());
                    lastTickerKey = await PublishSnapshotIfChangedAsync(lastTickerKey, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            lock (watcherGate)
            {
                stopping = true;
                watcher?.Dispose();
                watcher = null;
            }
        }
    }

    internal async Task PublishSwarmBusUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var freshBus = await snapshotService.TryRefreshActiveSwarmBusAsync(cancellationToken).ConfigureAwait(false);
            if (freshBus is not null)
            {
                await hubContext.Clients.All.SwarmBusUpdated(freshBus).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push live SwarmBus update over SignalR.");
        }
    }

    internal static string BuildSnapshotChangeKey(DashboardSnapshot snapshot)
    {
        var workflowKey = snapshot.Pipeline.WorkflowRuns is { Count: > 0 } runs
            ? string.Join(",", runs.Select(static r => $"{r.Repository}:{r.DatabaseId}:{r.Status}:{r.Conclusion ?? string.Empty}"))
            : string.Empty;

        var monitorKey = snapshot.Monitors.Monitors is { Count: > 0 } monitors
            ? string.Join(",", monitors.Select(static m =>
            {
                var subSummary = m.ActiveSubAgentsByModel is { Count: > 0 }
                    ? string.Join("+", m.ActiveSubAgentsByModel
                        .OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(static kv => $"{kv.Key}={kv.Value}"))
                    : string.Empty;
                return $"{m.Id}:{m.State}:{m.Model}:{m.ActiveSessionCount}:{m.ActiveSubAgentCount}:[{subSummary}]";
            }))
            : string.Empty;

        return string.Join('|',
            snapshot.OverallStatus,
            snapshot.WarningCount,
            snapshot.Pipeline.PullRequests.Count,
            snapshot.Pipeline.RecentlyMerged.FirstOrDefault()?.Number ?? 0,
            snapshot.Pipeline.ActiveIssues.Count,
            workflowKey,
            monitorKey,
            snapshot.SwarmBus?.UnreadCount ?? 0,
            snapshot.SwarmBus?.ActiveLockCount ?? 0);
    }

    private async Task<string> PublishSnapshotIfChangedAsync(string previousKey, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await snapshotService.GetSnapshotAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
            var key = BuildSnapshotChangeKey(snapshot);

            if (!string.Equals(key, previousKey, StringComparison.Ordinal))
            {
                await hubContext.Clients.All.SnapshotUpdated(snapshot).ConfigureAwait(false);
            }

            return key;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return previousKey;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Periodic SignalR snapshot check failed.");
            return previousKey;
        }
    }

    private int GetSnapshotIntervalSeconds() =>
        Math.Clamp(optionsMonitor.CurrentValue.GitHubCacheTtlSeconds, 15, 300);
}
