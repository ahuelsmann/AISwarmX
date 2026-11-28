using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AISwarmX.Dashboard.Api.Contracts;
using AISwarmX.Dashboard.Api.Providers;
using AISwarmX.Dashboard.Api.Services;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

public sealed class SignalRHubIntegrationTests
{
    [Fact]
    public async Task SwarmHubReceivesLiveSwarmBusUpdatesTriggeredByBroadcastAndSqliteChanges()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# AISwarmX\n");
        var dbPath = Path.Combine(fx.RootPath, "swarm-bus-v2.db");
        TempDashboardFixture.CreateSqliteDb(dbPath, """
            CREATE TABLE messages (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              created_at_utc TEXT NOT NULL,
              sender TEXT NOT NULL,
              recipient TEXT NOT NULL,
              topic TEXT NOT NULL,
              body TEXT NOT NULL,
              pr_number INTEGER,
              issue_number INTEGER,
              priority TEXT NOT NULL DEFAULT 'normal',
              reply_to_id INTEGER,
              status TEXT NOT NULL DEFAULT 'unread',
              acked_by TEXT
            );
            CREATE TABLE locks (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              agent TEXT NOT NULL,
              path_pattern TEXT NOT NULL,
              pr_number INTEGER,
              claimed_at_utc TEXT NOT NULL,
              expires_at_utc TEXT NOT NULL
            );
            INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status)
            VALUES ('2026-11-27T12:00:00Z', 'codex', 'claude-opus', 'init', 'Initial msg', 'unread');
            """);

        var runner = new PipelineAndWorktreeProviderTests.FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr")) return new CommandResult(0, "[]", string.Empty);
            if (fileName == "gh") return new CommandResult(1, string.Empty, "skip");
            if (fileName == "git" && args.Contains("worktree")) return new CommandResult(0, string.Empty, string.Empty);
            return new CommandResult(0, string.Empty, string.Empty);
        });

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:BindUrl"] = "http://127.0.0.1:5179",
                        ["Dashboard:MobaDevRepoPath"] = fx.MobaDevPath,
                        ["Dashboard:MobaFlowRepoPath"] = fx.MobaFlowPath,
                        ["Dashboard:SwarmBusDbPath"] = dbPath,
                        ["Dashboard:ClaudeHomePath"] = fx.ClaudeHomePath,
                        ["Dashboard:CodexHomePath"] = fx.CodexHomePath,
                        ["Dashboard:AntigravityHomePath"] = fx.AntigravityHomePath,
                        ["Dashboard:GeminiConfigPath"] = fx.GeminiConfigPath
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICommandRunner>();
                    services.AddSingleton<ICommandRunner>(runner);
                });
            });

        // Boot host before connecting SignalR client.
        using var httpClient = factory.CreateClient();
        var received = new TaskCompletionSource<SwarmBusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/swarm", options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        connection.On<SwarmBusResponse>("SwarmBusUpdated", bus =>
        {
            if (bus.RecentMessages.Any(m => m.Topic == "signalr-live-push"))
            {
                received.TrySetResult(bus);
            }
        });

        await connection.StartAsync();

        // Insert a second message into the SQLite DB and trigger broadcast.
        await using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO messages (created_at_utc, sender, recipient, topic, body, issue_number, priority, status)
                VALUES ('2026-11-27T12:01:00Z', 'antigravity-gemini', 'all', 'signalr-live-push', 'Realtime SignalR push verified', 56, 'high', 'unread');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // Wait for either FileSystemWatcher debounce or explicit host broadcast helper so CI stays fast and deterministic.
        var broadcaster = factory.Services
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<SwarmLiveBroadcastService>()
            .Single();
        await broadcaster.PublishSwarmBusUpdateAsync(CancellationToken.None);

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, pushed.UnreadCount);
        var pushedMsg = Assert.Single(pushed.RecentMessages, m => m.Topic == "signalr-live-push");
        Assert.Equal("antigravity-gemini", pushedMsg.Sender);
        Assert.Equal(56, pushedMsg.IssueNumber);

        // Also verify automatic FileSystemWatcher push (no manual PublishSwarmBusUpdateAsync call) and non-loopback Origin rejection.
        var watcherReceived = new TaskCompletionSource<SwarmBusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<SwarmBusResponse>("SwarmBusUpdated", bus =>
        {
            if (bus.RecentMessages.Any(m => m.Topic == "watcher-auto-push"))
            {
                watcherReceived.TrySetResult(bus);
            }
        });

        await using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO messages (created_at_utc, sender, recipient, topic, body, status)
                VALUES ('2026-11-27T12:02:00Z', 'claude-opus', 'codex', 'watcher-auto-push', 'Delivered by watcher', 'unread');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var watcherPushed = await watcherReceived.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(3, watcherPushed.UnreadCount);

        using var evilNegRequest = new HttpRequestMessage(HttpMethod.Post, "/hubs/swarm/negotiate?negotiateVersion=1");
        evilNegRequest.Headers.Add("Origin", "http://evil.example");
        var evilResponse = await httpClient.SendAsync(evilNegRequest);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, evilResponse.StatusCode);

        using var badHostRequest = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        badHostRequest.Headers.Host = "evil.example:5179";
        var badHostResponse = await httpClient.SendAsync(badHostRequest);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, badHostResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshSwarmBusAsyncDoesNotOverwriteCacheAfterWorkspaceChangesInFlight()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# AISwarmX\n");
        var dbA = Path.Combine(fx.RootPath, "swarm-a.db");
        var dbB = Path.Combine(fx.RootPath, "swarm-b.db");

        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var optionsA = new Configuration.DashboardOptions
        {
            BindUrl = "http://127.0.0.1:5179",
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            SwarmBusDbPath = dbA,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            LocalCacheTtlSeconds = 60
        };
        var monitor = new MutableOptionsMonitor<Configuration.DashboardOptions>(optionsA);
        var fakeSwarmBusProvider = new BlockingWorkspaceSwarmBusProvider(dbA, startedA, releaseA);

        var runner = new PipelineAndWorktreeProviderTests.FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr")) return new CommandResult(0, "[]", string.Empty);
            if (fileName == "gh") return new CommandResult(1, string.Empty, "skip");
            if (fileName == "git" && args.Contains("worktree")) return new CommandResult(0, string.Empty, string.Empty);
            return new CommandResult(0, string.Empty, string.Empty);
        });

        var service = new DashboardSnapshotService(
            monitor,
            new ClaudeMonitorProvider(),
            new CodexMonitorProvider(),
            new AntigravityMonitorProvider(),
            new GovernanceProvider(),
            new ConfigDocsProvider(),
            new PipelineProvider(runner),
            new WorktreeProvider(runner),
            fakeSwarmBusProvider);

        var pushedBroadcasts = new System.Collections.Concurrent.ConcurrentQueue<SwarmBusResponse>();
        var hubContext = new RecordingHubContext(pushedBroadcasts);
        var broadcaster = new SwarmLiveBroadcastService(
            service,
            hubContext,
            monitor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SwarmLiveBroadcastService>.Instance);

        var publishATask = broadcaster.PublishSwarmBusUpdateAsync(CancellationToken.None);
        await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Switch active workspace to dbB and populate the cache while dbA's refresh/publish is still in flight.
        monitor.Update(new Configuration.DashboardOptions
        {
            BindUrl = "http://127.0.0.1:5179",
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            SwarmBusDbPath = dbB,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            LocalCacheTtlSeconds = 60
        });

        var snapshotB = await service.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);
        Assert.Equal(22, snapshotB.SwarmBus!.UnreadCount);

        // Release stale dbA refresh: obsolete SignalR broadcast must be suppressed and cached B preserved.
        releaseA.SetResult();
        await publishATask;
        Assert.Empty(pushedBroadcasts);

        var cachedAfter = await service.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);
        Assert.Equal(22, cachedAfter.SwarmBus!.UnreadCount);

        // Next active refresh/broadcast belongs to dbB (22 unread) and is delivered.
        await broadcaster.PublishSwarmBusUpdateAsync(CancellationToken.None);
        var delivered = Assert.Single(pushedBroadcasts);
        Assert.Equal(22, delivered.UnreadCount);

        // Also verify HTTP RefreshSwarmBusAsync retries onto dbB instead of falling back to stale dbA when workspace switches in flight.
        var resetAStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetARelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Update(new Configuration.DashboardOptions
        {
            BindUrl = "http://127.0.0.1:5179",
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            SwarmBusDbPath = dbA,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            LocalCacheTtlSeconds = 60
        });
        fakeSwarmBusProvider.ResetBarrier(resetAStarted, resetARelease);

        var httpRefreshTask = service.RefreshSwarmBusAsync(CancellationToken.None);
        await resetAStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        monitor.Update(new Configuration.DashboardOptions
        {
            BindUrl = "http://127.0.0.1:5179",
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            SwarmBusDbPath = dbB,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            LocalCacheTtlSeconds = 60
        });
        await service.GetSnapshotAsync(forceRefresh: true, CancellationToken.None);

        resetARelease.SetResult();
        var httpResult = await httpRefreshTask;
        Assert.Equal(22, httpResult.UnreadCount);

        // Exhausting all 8 bounded refresh attempts due to continuous workspace switching raises SwarmBusContentionException and /api/bus maps it to HTTP 503 + Retry-After: 1.
        await using var contentionFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:BindUrl"] = "http://127.0.0.1:5179",
                        ["Dashboard:MobaDevRepoPath"] = fx.MobaDevPath,
                        ["Dashboard:MobaFlowRepoPath"] = fx.MobaFlowPath,
                        ["Dashboard:SwarmBusDbPath"] = dbA,
                        ["Dashboard:ClaudeHomePath"] = fx.ClaudeHomePath,
                        ["Dashboard:CodexHomePath"] = fx.CodexHomePath,
                        ["Dashboard:AntigravityHomePath"] = fx.AntigravityHomePath,
                        ["Dashboard:GeminiConfigPath"] = fx.GeminiConfigPath
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICommandRunner>();
                    services.AddSingleton<ICommandRunner>(runner);
                    services.RemoveAll<Microsoft.Extensions.Options.IOptionsMonitor<Configuration.DashboardOptions>>();
                    var churnMonitor = new MutableOptionsMonitor<Configuration.DashboardOptions>(optionsA);
                    services.AddSingleton<Microsoft.Extensions.Options.IOptionsMonitor<Configuration.DashboardOptions>>(churnMonitor);
                    services.RemoveAll<SwarmBusProvider>();
                    services.AddSingleton<SwarmBusProvider>(new WorkspaceChurnSwarmBusProvider(churnMonitor, fx));
                });
            });

        using var contentionClient = contentionFactory.CreateClient();
        var busResponse = await contentionClient.GetAsync("/api/bus?refresh=true");
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, busResponse.StatusCode);
        Assert.Contains("1", busResponse.Headers.GetValues("Retry-After"));
    }

    private sealed class WorkspaceChurnSwarmBusProvider(
        MutableOptionsMonitor<Configuration.DashboardOptions> monitor,
        TempDashboardFixture fx) : SwarmBusProvider
    {
        private int _seq;

        public override Task<SwarmBusResponse> GetSwarmBusAsync(
            Configuration.ResolvedDashboardPaths paths,
            CancellationToken cancellationToken)
        {
            var next = Interlocked.Increment(ref _seq);
            monitor.Update(new Configuration.DashboardOptions
            {
                BindUrl = "http://127.0.0.1:5179",
                MobaDevRepoPath = fx.MobaDevPath,
                MobaFlowRepoPath = fx.MobaFlowPath,
                SwarmBusDbPath = Path.Combine(fx.RootPath, $"swarm-churn-{next}.db"),
                ClaudeHomePath = fx.ClaudeHomePath,
                CodexHomePath = fx.CodexHomePath,
                AntigravityHomePath = fx.AntigravityHomePath,
                GeminiConfigPath = fx.GeminiConfigPath,
                LocalCacheTtlSeconds = 60
            });
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new SwarmBusResponse(now, next, 0, [], [], new SourceHealth("ok", now, "churn")));
        }
    }

    private sealed class RecordingHubContext(
        System.Collections.Concurrent.ConcurrentQueue<SwarmBusResponse> queue)
        : Microsoft.AspNetCore.SignalR.IHubContext<Hubs.SwarmDashboardHub, Hubs.ISwarmDashboardClient>
    {
        private readonly Hubs.ISwarmDashboardClient _all = new RecordingClient(queue);
        public Microsoft.AspNetCore.SignalR.IHubClients<Hubs.ISwarmDashboardClient> Clients => new RecordingClients(_all);
        public Microsoft.AspNetCore.SignalR.IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(Hubs.ISwarmDashboardClient all)
            : Microsoft.AspNetCore.SignalR.IHubClients<Hubs.ISwarmDashboardClient>
        {
            public Hubs.ISwarmDashboardClient All => all;
            public Hubs.ISwarmDashboardClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => all;
            public Hubs.ISwarmDashboardClient Client(string connectionId) => all;
            public Hubs.ISwarmDashboardClient Clients(IReadOnlyList<string> connectionIds) => all;
            public Hubs.ISwarmDashboardClient Group(string groupName) => all;
            public Hubs.ISwarmDashboardClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => all;
            public Hubs.ISwarmDashboardClient Groups(IReadOnlyList<string> groupNames) => all;
            public Hubs.ISwarmDashboardClient User(string userId) => all;
            public Hubs.ISwarmDashboardClient Users(IReadOnlyList<string> userIds) => all;
        }

        private sealed class RecordingClient(System.Collections.Concurrent.ConcurrentQueue<SwarmBusResponse> queue)
            : Hubs.ISwarmDashboardClient
        {
            public Task SwarmBusUpdated(SwarmBusResponse swarmBus)
            {
                queue.Enqueue(swarmBus);
                return Task.CompletedTask;
            }

            public Task SnapshotUpdated(DashboardSnapshot snapshot) => Task.CompletedTask;
        }
    }

    private sealed class BlockingWorkspaceSwarmBusProvider(
        string dbA,
        TaskCompletionSource startedA,
        TaskCompletionSource releaseA) : SwarmBusProvider
    {
        private TaskCompletionSource _startedA = startedA;
        private TaskCompletionSource _releaseA = releaseA;

        public void ResetBarrier(TaskCompletionSource nextStarted, TaskCompletionSource nextRelease)
        {
            _startedA = nextStarted;
            _releaseA = nextRelease;
        }

        public override async Task<SwarmBusResponse> GetSwarmBusAsync(
            Configuration.ResolvedDashboardPaths paths,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            if (string.Equals(paths.SwarmBusDbPath, dbA, StringComparison.OrdinalIgnoreCase))
            {
                _startedA.TrySetResult();
                await _releaseA.Task.WaitAsync(cancellationToken);
                return new SwarmBusResponse(now, 11, 0, [], [], new SourceHealth("ok", now, "A"));
            }

            return new SwarmBusResponse(now, 22, 0, [], [], new SourceHealth("ok", now, "B"));
        }
    }

    private sealed class MutableOptionsMonitor<T>(T initial) : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private T _current = initial;
        public T CurrentValue => _current;
        public T Get(string? name) => _current;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
        public void Update(T next) => _current = next;
    }
}
