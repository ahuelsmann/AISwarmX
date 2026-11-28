using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AISwarmX.Dashboard.Api.Contracts;
using AISwarmX.Dashboard.Api.Providers;
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
