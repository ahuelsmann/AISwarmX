using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Providers;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed class PipelineAndWorktreeProviderTests
{
    [Fact]
    public async Task PipelineProvider_ParsesPrsHandlesMissingProjectScopeAndCachesFallbackWhenOffline()
    {
        var prJson = """
            [
              {
                "number": 492,
                "title": "docs(agents): track slices on Kanban",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/492",
                "headRefName": "codex/strategy-mobaflow-kanban-board",
                "isDraft": true,
                "mergeStateStatus": "BEHIND",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-18T20:45:00Z",
                "body": "## Intent (Class L documentation slice)\nLocked areas: `.github/instructions`, `AGENTS.md`",
                "statusCheckRollup": [
                  { "name": "Repository instruction consistency", "status": "COMPLETED", "conclusion": "SUCCESS" },
                  { "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "SUCCESS" }
                ]
              }
            ]
            """;

        var failGitHub = false;
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr"))
            {
                return failGitHub
                    ? new CommandResult(1, string.Empty, "network offline")
                    : new CommandResult(0, prJson, string.Empty);
            }

            if (fileName == "gh" && args.Contains("project"))
            {
                return new CommandResult(1, string.Empty, "missing required scopes [read:project]");
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var provider = new PipelineProvider(runner);
        var options = new DashboardOptions();

        // First call: PRs succeed, project scope missing -> graceful partial notice
        var first = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Equal("ok", first.Source.Status);
        Assert.False(first.ProjectBoardAvailable);
        Assert.Contains("read:project", first.ProjectBoardNotice);

        var pr = Assert.Single(first.PullRequests);
        Assert.Equal(492, pr.Number);
        Assert.Equal("Draft", pr.StateLabel);
        Assert.Equal("BEHIND", pr.MergeState);
        Assert.Equal("pass", pr.CiStatus);
        Assert.Equal("pass", pr.SonarStatus);
        Assert.Equal("L", pr.RiskClass);
        Assert.Equal([".github/instructions", "AGENTS.md"], pr.LockedAreas);

        // Second call: GitHub fails -> cached fallback with degraded status
        failGitHub = true;
        var second = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Equal("degraded", second.Source.Status);
        Assert.True(second.Source.FromCache);
        Assert.Single(second.PullRequests);
    }

    [Fact]
    public async Task WorktreeProvider_ClassifiesMainActiveAndStaleAndSortsStaleFirst()
    {
        using var fx = new TempDashboardFixture();
        var porcelain = $"""
            worktree {fx.MobaFlowPath.Replace('\\', '/')}
            HEAD 72a78bd01ae219358776e665444550a1353ea2b2
            branch refs/heads/main

            worktree {fx.MobaFlowPath.Replace('\\', '/')}/artifacts/worktrees/old-merged
            HEAD a12e606311111111111111111111111111111111
            branch refs/heads/codex/old-merged

            worktree {fx.MobaFlowPath.Replace('\\', '/')}/artifacts/worktrees/kanban
            HEAD 367a522511111111111111111111111111111111
            branch refs/heads/codex/strategy-mobaflow-kanban-board
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
            fileName == "git" && args.Contains("worktree")
                ? new CommandResult(0, porcelain, string.Empty)
                : new CommandResult(1, string.Empty, "unexpected"));

        var provider = new WorktreeProvider(runner);
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var openPrs = new[]
        {
            new Contracts.PullRequestInfo(
                492, "docs", "https://github.com/ahuelsmann/MOBAflow/pull/492",
                "codex/strategy-mobaflow-kanban-board", "Draft", true, false,
                "BEHIND", "pass", "pass", "L", [], DateTimeOffset.UtcNow, null)
        };

        var (worktrees, health) = await provider.GetWorktreesAsync(paths, openPrs, CancellationToken.None);

        Assert.Equal("ok", health.Status);
        Assert.Equal(3, worktrees.Count);
        Assert.Equal("stale", worktrees[0].Status); // stale sorted first
        Assert.Equal("active", worktrees[1].Status);
        Assert.Equal(492, worktrees[1].LinkedPrNumber);
        Assert.Equal("main", worktrees[2].Status);
        Assert.True(worktrees[2].IsMain);
    }

    internal sealed class FakeCommandRunner : ICommandRunner
    {
        private readonly Func<string, IReadOnlyList<string>, CommandResult> _handler;

        public FakeCommandRunner(Func<string, IReadOnlyList<string>, CommandResult> handler)
        {
            _handler = handler;
        }

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(fileName, arguments));
        }
    }
}
