using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;
using AISwarmX.Dashboard.Api.Providers;
using AISwarmX.Dashboard.Api.Services;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

public sealed class ApiEndpointsIntegrationTests
{
    [Fact]
    public async Task AllGetEndpointsReturnOkAndPostIsRejectedAsReadOnly()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\nLine 1\n");
        await File.WriteAllTextAsync(
            Path.Combine(fx.MobaDevPath, "CHANGELOG.md"),
            "# Changelog\n\n## [3.5.5] - 2026-11-20 (PR #22)\n");

        var repoAgyDir = Path.Combine(fx.MobaDevPath, "agents", "antigravity", "agents");
        var localAgyDir = Path.Combine(fx.GeminiConfigPath, "agents");
        Directory.CreateDirectory(repoAgyDir);
        Directory.CreateDirectory(localAgyDir);
        await File.WriteAllTextAsync(Path.Combine(repoAgyDir, "head-of-ai-strategy.md"), "Line A\n");
        await File.WriteAllTextAsync(Path.Combine(localAgyDir, "head-of-ai-strategy.md"), "Line A\nLine B\n");

        var runner = new PipelineAndWorktreeProviderTests.FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && (args.Contains("project") || args.Contains("graphql")))
            {
                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "git" && args.Contains("worktree"))
            {
                return new CommandResult(
                    0,
                    $"worktree {fx.MobaFlowPath.Replace('\\', '/')}\nHEAD abcdef123456\nbranch refs/heads/main\n",
                    string.Empty);
            }

            return new CommandResult(1, string.Empty, "unexpected");
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

        using var client = factory.CreateClient();

        var healthRes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
        var health = await healthRes.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.True(health.ReadOnly);
        Assert.Equal("http://127.0.0.1:5179", health.BindAddress);

        var snapshotRes = await client.GetAsync("/api/snapshot");
        Assert.Equal(HttpStatusCode.OK, snapshotRes.StatusCode);
        var snapshot = await snapshotRes.Content.ReadFromJsonAsync<DashboardSnapshot>();
        Assert.NotNull(snapshot);
        Assert.True(snapshot.ReadOnly);
        Assert.Equal("3.5.5", snapshot.ChangelogVersion);
        Assert.Equal(4, snapshot.Monitors.Monitors.Count);
        Assert.Equal(
            ["codex-monitor", "claude-monitor", "antigravity-monitor", "antigravity-opus-head"],
            snapshot.Monitors.Monitors.Select(m => m.Id));
        Assert.Equal(
            ["AiSwarmX ChatGPT Codex", "AiSwarmX Claude Opus", "AiSwarmX Antigravity Gemini", "AiSwarmX Antigravity Opus"],
            snapshot.Monitors.Monitors.Select(m => m.DisplayName));
        Assert.NotNull(snapshot.Monitors.Summary);
        Assert.Equal(4, snapshot.Monitors.Summary.StandingMonitorsTotal);
        Assert.Single(snapshot.Hygiene.Worktrees);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/monitors")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/governance")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pipeline")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/hygiene")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bus")).StatusCode);

        var diffRes = await client.GetAsync("/api/governance/diff?tool=Antigravity&kind=agent&fileName=head-of-ai-strategy.md");
        Assert.Equal(HttpStatusCode.OK, diffRes.StatusCode);
        var diffPayload = await diffRes.Content.ReadFromJsonAsync<RuleDiffResponse>();
        Assert.NotNull(diffPayload);
        Assert.Equal(1, diffPayload.AddedLines);

        var badDiffRes = await client.GetAsync("/api/governance/diff?tool=Antigravity&kind=agent&fileName=../secret.md");
        Assert.Equal(HttpStatusCode.BadRequest, badDiffRes.StatusCode);

        var docsRes = await client.GetAsync("/api/docs?scope=aiswarmx");
        Assert.Equal(HttpStatusCode.OK, docsRes.StatusCode);
        var catalog = await docsRes.Content.ReadFromJsonAsync<ConfigDocsCatalogResponse>();
        Assert.NotNull(catalog);
        Assert.Equal("aiswarmx", catalog.Scope);
        Assert.Equal("ahuelsmann/AISwarmX", catalog.Repository);
        Assert.Contains(catalog.Files, f => f.RelativePath == "manifest.md");

        var legacyDocsRes = await client.GetAsync("/api/docs?scope=mobadev");
        Assert.Equal(HttpStatusCode.OK, legacyDocsRes.StatusCode);
        var legacyCatalog = await legacyDocsRes.Content.ReadFromJsonAsync<ConfigDocsCatalogResponse>();
        Assert.NotNull(legacyCatalog);
        Assert.Equal("aiswarmx", legacyCatalog.Scope);
        Assert.Equal("ahuelsmann/AISwarmX", legacyCatalog.Repository);

        var docContentRes = await client.GetAsync("/api/docs/content?scope=aiswarmx&path=manifest.md");
        Assert.Equal(HttpStatusCode.OK, docContentRes.StatusCode);
        var docContent = await docContentRes.Content.ReadFromJsonAsync<ConfigDocContentResponse>();
        Assert.NotNull(docContent);
        Assert.Contains("# MOBAdev", docContent.Content);

        var badDocRes = await client.GetAsync("/api/docs/content?scope=mobadev&path=../secret.md");
        Assert.Equal(HttpStatusCode.BadRequest, badDocRes.StatusCode);

        // Strictly read-only: POST must be rejected
        using var postRes = await client.PostAsJsonAsync("/api/snapshot", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, postRes.StatusCode);
    }

    [Fact]
    public async Task WebApplicationFactory_AppliesSwarmConfigWorkspaceAndPreservesExplicitOverrides()
    {
        using var fx = new TempDashboardFixture();
        var repoRoot = FindWorktreeRoot();
        var presetPath = Path.Combine(repoRoot, "presets", "mobaflow.swarm.json");

        var runner = new PipelineAndWorktreeProviderTests.FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr"))
            {
                return new CommandResult(1, string.Empty, "Pull requests are disabled for this repository.");
            }

            if (fileName == "gh" && args.Contains("graphql"))
            {
                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:SwarmConfigPath"] = presetPath,
                        ["Dashboard:WorkspaceId"] = "mobadev",
                        ["Dashboard:MobaDevRepoPath"] = fx.MobaDevPath,
                        ["Dashboard:MobaFlowRepoPath"] = fx.MobaFlowPath,
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

        using var client = factory.CreateClient();
        var pipelineRes = await client.GetAsync("/api/pipeline");
        Assert.Equal(HttpStatusCode.OK, pipelineRes.StatusCode);
        var pipeline = await pipelineRes.Content.ReadFromJsonAsync<PipelineResponse>();
        Assert.NotNull(pipeline);
        Assert.Equal("ahuelsmann/MOBAdev", pipeline.Repository);
        Assert.Equal("ok", pipeline.Source.Status);
        Assert.Empty(pipeline.PullRequests);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsImmediatelyFromCacheWhenTtlExpiredAndConcurrentGhCommandBlocks()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\n");

        var blockGhCommands = false;
        var ghEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGhTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new AsyncDelegateCommandRunner(async (fileName, args, ct) =>
        {
            if (fileName == "gh")
            {
                if (Volatile.Read(ref blockGhCommands))
                {
                    ghEnteredTcs.TrySetResult();
                    await releaseGhTcs.Task.WaitAsync(ct);
                }

                if (args.Contains("pr") || args.Contains("issue") || args.Contains("run"))
                {
                    return new CommandResult(0, "[]", string.Empty);
                }

                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "git" && args.Contains("worktree"))
            {
                return new CommandResult(
                    0,
                    $"worktree {fx.MobaFlowPath.Replace('\\', '/')}\nHEAD abcdef123456\nbranch refs/heads/main\n",
                    string.Empty);
            }

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

        using var client = factory.CreateClient();

        // 1. Warm the snapshot cache
        var warmRes = await client.GetAsync("/api/snapshot");
        Assert.Equal(HttpStatusCode.OK, warmRes.StatusCode);

        // 2. Expire TTLs and block subsequent gh CLI invocations
        var snapshotService = factory.Services.GetRequiredService<DashboardSnapshotService>();
        snapshotService.ExpireCachesForTesting();
        Volatile.Write(ref blockGhCommands, true);

        // Start a concurrent /api/pipeline request so _pipelineLock is held by a slow gh subprocess
        var blockedPipelineTask = client.GetAsync("/api/pipeline");
        await ghEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            // 3. /api/health must return 200 OK in < 500ms without waiting on _pipelineLock
            var sw = Stopwatch.StartNew();
            var healthRes = await client.GetAsync("/api/health");
            sw.Stop();

            Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
            Assert.True(sw.ElapsedMilliseconds < 500, $"Expected /api/health < 500ms, actual {sw.ElapsedMilliseconds}ms.");
            var health = await healthRes.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(health);
            Assert.Contains("pipeline", health.Sources.Keys);
            Assert.Contains("monitors", health.Sources.Keys);
        }
        finally
        {
            releaseGhTcs.TrySetResult();
            await blockedPipelineTask;
        }
    }

    [Fact]
    public async Task HealthEndpoint_NonCooperativeColdProvider_ReturnsStartingWithinColdWaitBudget()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\n");

        var ghEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseColdGhTcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new AsyncDelegateCommandRunner(async (fileName, _, _) =>
        {
            if (fileName == "gh")
            {
                ghEnteredTcs.TrySetResult();
                // Deliberately ignore CancellationToken to simulate a non-cooperative / hung cold provider
                return await releaseColdGhTcs.Task;
            }

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

        using var client = factory.CreateClient();
        var snapshotService = factory.Services.GetRequiredService<DashboardSnapshotService>();
        snapshotService.ColdHealthWaitBudget = TimeSpan.FromMilliseconds(120);

        try
        {
            var sw = Stopwatch.StartNew();
            var healthRes = await client.GetAsync("/api/health");
            sw.Stop();

            Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
            Assert.True(sw.ElapsedMilliseconds < 800, $"Expected cold /api/health < 800ms, actual {sw.ElapsedMilliseconds}ms.");
            Assert.False(releaseColdGhTcs.Task.IsCompleted);
            await ghEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var health = await healthRes.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(health);
            Assert.Equal("starting", health.Status);
            Assert.Empty(health.Sources);
            Assert.Equal("http://127.0.0.1:5179", health.BindAddress);
        }
        finally
        {
            releaseColdGhTcs.TrySetResult(new CommandResult(0, "[]", string.Empty));
            await snapshotService.WaitForInflightHealthRefreshForTestingAsync();
        }
    }

    [Fact]
    public async Task HealthEndpoint_ExpiredWarmCacheWithNoLockHolder_ReturnsCachedImmediatelyAndRefreshesOffThread()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\n");

        var blockGhCommands = false;
        var backgroundRefreshEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBackgroundGhTcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new AsyncDelegateCommandRunner(async (fileName, args, _) =>
        {
            if (fileName == "gh")
            {
                if (Volatile.Read(ref blockGhCommands))
                {
                    backgroundRefreshEnteredTcs.TrySetResult();
                    // Non-cooperative wait while _pipelineLock & _localLock were completely free before /api/health arrived
                    return await releaseBackgroundGhTcs.Task;
                }

                if (args.Contains("pr") || args.Contains("issue") || args.Contains("run"))
                {
                    return new CommandResult(0, "[]", string.Empty);
                }

                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "git" && args.Contains("worktree"))
            {
                return new CommandResult(
                    0,
                    $"worktree {fx.MobaFlowPath.Replace('\\', '/')}\nHEAD abcdef123456\nbranch refs/heads/main\n",
                    string.Empty);
            }

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

        using var client = factory.CreateClient();

        var warmSnapshot = await (await client.GetAsync("/api/snapshot")).Content.ReadFromJsonAsync<DashboardSnapshot>();
        Assert.NotNull(warmSnapshot);

        var snapshotService = factory.Services.GetRequiredService<DashboardSnapshotService>();
        snapshotService.ExpireCachesForTesting();
        Volatile.Write(ref blockGhCommands, true);

        try
        {
            // Neither _pipelineLock nor _localLock is held when /api/health arrives
            var sw = Stopwatch.StartNew();
            var healthRes = await client.GetAsync("/api/health");
            sw.Stop();

            Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
            Assert.True(sw.ElapsedMilliseconds < 200, $"Expected warm expired /api/health < 200ms, actual {sw.ElapsedMilliseconds}ms.");

            var health = await healthRes.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(health);
            Assert.Equal(warmSnapshot.OverallStatus, health.Status);
            Assert.Equal(warmSnapshot.GeneratedAtUtc, health.GeneratedAtUtc);
            Assert.Contains("pipeline", health.Sources.Keys);

            // Confirm coalesced Task.Run background refresh was kicked off asynchronously
            await backgroundRefreshEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseBackgroundGhTcs.TrySetResult(new CommandResult(0, "[]", string.Empty));
            await snapshotService.WaitForInflightHealthRefreshForTestingAsync();
        }
    }

    [Fact]
    public async Task HealthEndpoint_WorkspaceSwitchFromWarmAToColdB_DoesNotLeakWorkspaceASources()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\n");

        DashboardOptions CreateOptions(string repo, string bindUrl) => new()
        {
            BindUrl = bindUrl,
            GitHubRepository = repo,
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath
        };

        var mutableOptions = new MutableOptionsMonitor<DashboardOptions>(
            CreateOptions("ahuelsmann/MOBAflow", "http://127.0.0.1:5179"));

        var blockWorkspaceB = false;
        var sentiXEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSentiXTcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new AsyncDelegateCommandRunner(async (fileName, args, _) =>
        {
            if (fileName == "gh")
            {
                if (Volatile.Read(ref blockWorkspaceB) &&
                    args.Any(a => a.Contains("ahuelsmann/SentiX", StringComparison.OrdinalIgnoreCase)))
                {
                    sentiXEnteredTcs.TrySetResult();
                    return await releaseSentiXTcs.Task;
                }

                if (args.Contains("pr") || args.Contains("issue") || args.Contains("run"))
                {
                    return new CommandResult(0, "[]", string.Empty);
                }

                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "git" && args.Contains("worktree"))
            {
                return new CommandResult(
                    0,
                    $"worktree {fx.MobaFlowPath.Replace('\\', '/')}\nHEAD abcdef123456\nbranch refs/heads/main\n",
                    string.Empty);
            }

            return new CommandResult(0, string.Empty, string.Empty);
        });

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IOptionsMonitor<DashboardOptions>>();
                    services.AddSingleton<IOptionsMonitor<DashboardOptions>>(mutableOptions);
                    services.RemoveAll<ICommandRunner>();
                    services.AddSingleton<ICommandRunner>(runner);
                });
            });

        using var client = factory.CreateClient();
        var snapshotService = factory.Services.GetRequiredService<DashboardSnapshotService>();

        // 1. Warm Workspace A ("ahuelsmann/MOBAflow") deterministically, then verify /api/health
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/snapshot")).StatusCode);
        var warmARes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, warmARes.StatusCode);
        var warmA = await warmARes.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(warmA);
        Assert.NotEqual("starting", warmA.Status);
        Assert.Contains("pipeline", warmA.Sources.Keys);

        // 2. Switch to cold Workspace B ("ahuelsmann/SentiX") whose gh call blocks
        snapshotService.ColdHealthWaitBudget = TimeSpan.FromMilliseconds(120);
        Volatile.Write(ref blockWorkspaceB, true);
        mutableOptions.Update(CreateOptions("ahuelsmann/SentiX", "http://127.0.0.1:5180"));

        try
        {
            var coldBRes = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, coldBRes.StatusCode);
            var coldB = await coldBRes.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(coldB);
            Assert.Equal("starting", coldB.Status);
            Assert.Empty(coldB.Sources);
            Assert.Equal("http://127.0.0.1:5180", coldB.BindAddress);

            await sentiXEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseSentiXTcs.TrySetResult(new CommandResult(0, "[]", string.Empty));
            await snapshotService.WaitForInflightHealthRefreshForTestingAsync();
        }

        // 3. Once Workspace B unblocks, /api/health returns Workspace B's coherent health
        var warmBRes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, warmBRes.StatusCode);
        var warmB = await warmBRes.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(warmB);
        Assert.NotEqual("starting", warmB.Status);
        Assert.Equal("http://127.0.0.1:5180", warmB.BindAddress);
        Assert.Contains("pipeline", warmB.Sources.Keys);
    }

    [Fact]
    public async Task HealthEndpoint_CooperativelyStalledBackgroundRefresh_TimesOutReleasesSlotAndRecovers()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\n");

        var stallCooperatively = true;
        var stallEnteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallCanceledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new AsyncDelegateCommandRunner(async (fileName, args, ct) =>
        {
            if (fileName == "gh")
            {
                if (Volatile.Read(ref stallCooperatively))
                {
                    stallEnteredTcs.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        stallCanceledTcs.TrySetResult();
                        throw;
                    }
                }

                if (args.Contains("pr") || args.Contains("issue") || args.Contains("run"))
                {
                    return new CommandResult(0, "[]", string.Empty);
                }

                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "git" && args.Contains("worktree"))
            {
                return new CommandResult(
                    0,
                    $"worktree {fx.MobaFlowPath.Replace('\\', '/')}\nHEAD abcdef123456\nbranch refs/heads/main\n",
                    string.Empty);
            }

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

        using var client = factory.CreateClient();
        var snapshotService = factory.Services.GetRequiredService<DashboardSnapshotService>();
        snapshotService.ColdHealthWaitBudget = TimeSpan.FromMilliseconds(80);
        snapshotService.BackgroundHealthRefreshTimeout = TimeSpan.FromMilliseconds(200);

        // 1. First /api/health starts background refresh that stalls cooperatively on ct
        var startingRes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, startingRes.StatusCode);
        var startingHealth = await startingRes.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(startingHealth);
        Assert.Equal("starting", startingHealth.Status);

        // 2. Service-owned BackgroundHealthRefreshTimeout cancels the stalled refresh and clears the slot
        await stallEnteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stallCanceledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await snapshotService.WaitForInflightHealthRefreshForTestingAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // 3. Once provider recovers, next /api/health starts a fresh refresh and returns healthy
        Volatile.Write(ref stallCooperatively, false);
        snapshotService.ColdHealthWaitBudget = TimeSpan.FromMilliseconds(1500);
        snapshotService.BackgroundHealthRefreshTimeout = TimeSpan.FromSeconds(10);

        var recoveredRes = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, recoveredRes.StatusCode);
        var recoveredHealth = await recoveredRes.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(recoveredHealth);
        Assert.NotEqual("starting", recoveredHealth.Status);
        Assert.Contains("pipeline", recoveredHealth.Sources.Keys);
        Assert.Contains("monitors", recoveredHealth.Sources.Keys);
    }

    [Fact]
    public async Task HealthEndpoint_ConcurrentSnapshotAndSwarmBusRefresh_SerializesHealthPublicationUnderLocalLock()
    {
        using var fx = new TempDashboardFixture();
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev\n");

        var busNotice = "swarmbus-initial-v1";
        var busProvider = new ControllableSwarmBusProvider(() =>
        {
            var currentNotice = Volatile.Read(ref busNotice);
            var now = DateTimeOffset.UtcNow;
            return new SwarmBusResponse(
                GeneratedAtUtc: now,
                UnreadCount: 0,
                ActiveLockCount: 0,
                RecentMessages: [],
                ActiveLocks: [],
                Source: new SourceHealth("ok", now, currentNotice));
        });

        var runner = new AsyncDelegateCommandRunner((fileName, args, _) =>
        {
            if (fileName == "gh" && (args.Contains("pr") || args.Contains("issue") || args.Contains("run")))
            {
                return Task.FromResult(new CommandResult(0, "[]", string.Empty));
            }

            if (fileName == "git" && args.Contains("worktree"))
            {
                return Task.FromResult(new CommandResult(
                    0,
                    $"worktree {fx.MobaFlowPath.Replace('\\', '/')}\nHEAD abcdef123456\nbranch refs/heads/main\n",
                    string.Empty));
            }

            return Task.FromResult(new CommandResult(1, string.Empty, "missing scope"));
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
                        ["Dashboard:LocalCacheTtlSeconds"] = "60",
                        ["Dashboard:GitHubCacheTtlSeconds"] = "60",
                        ["Dashboard:MobaDevRepoPath"] = fx.MobaDevPath,
                        ["Dashboard:MobaFlowRepoPath"] = fx.MobaFlowPath,
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
                    services.RemoveAll<SwarmBusProvider>();
                    services.AddSingleton<SwarmBusProvider>(busProvider);
                });
            });

        using var client = factory.CreateClient();
        var snapshotService = factory.Services.GetRequiredService<DashboardSnapshotService>();

        // 1. Warm initial snapshot with "swarmbus-initial-v1"
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/snapshot")).StatusCode);
        var initialHealth = await (await client.GetAsync("/api/health")).Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(initialHealth);
        Assert.Equal("swarmbus-initial-v1", initialHealth.Sources["swarmBus"].Notice);

        // 2. Race concurrent GetSnapshotAsync(forceRefresh: false) calls against RefreshSwarmBusAsync("swarmbus-fresh-v2")
        // Use async TaskCompletionSource so zero ThreadPool worker threads block synchronously.
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref busNotice, "swarmbus-fresh-v2");

        var snapshotRacers = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(async () =>
            {
                await startGate.Task;
                await snapshotService.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);
            }))
            .ToArray();

        var busRefreshTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await snapshotService.RefreshSwarmBusAsync(CancellationToken.None);
        });

        startGate.TrySetResult();
        var refreshedBus = await busRefreshTask;
        Assert.Equal("swarmbus-fresh-v2", refreshedBus.Source.Notice);
        await Task.WhenAll(snapshotRacers);

        // 3. Because _cachedHealthEntry publication is serialized under _localLock, no racing snapshot can overwrite v2 back to v1
        var finalHealth = await (await client.GetAsync("/api/health")).Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(finalHealth);
        Assert.Equal("swarmbus-fresh-v2", finalHealth.Sources["swarmBus"].Notice);
    }

    private sealed class ControllableSwarmBusProvider(Func<SwarmBusResponse> factory) : SwarmBusProvider
    {
        public override Task<SwarmBusResponse> GetSwarmBusAsync(
            ResolvedDashboardPaths paths,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(factory());
        }
    }

    private sealed class MutableOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
        where T : class
    {
        private T _current = initial;

        public T CurrentValue => Volatile.Read(ref _current);

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;

        public void Update(T next) => Volatile.Write(ref _current, next);
    }

    private sealed class AsyncDelegateCommandRunner(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<CommandResult>> handler) : ICommandRunner
    {
        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            handler(fileName, arguments, cancellationToken);
    }

    private static string FindWorktreeRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "presets", "mobaflow.swarm.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate worktree root containing presets/mobaflow.swarm.json.");
    }
}
