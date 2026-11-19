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

        var mergedPrJson = """
            [
              {
                "number": 495,
                "title": "build(deps): bump patch packages",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/495",
                "headRefName": "claude/deps-494",
                "mergedAt": "2026-11-19T12:00:00Z",
                "updatedAt": "2026-11-19T12:00:00Z",
                "body": "Class L dependency update.\n🤖 Generated with [Claude Code](https://claude.com/claude-code)",
                "comments": [
                  {
                    "author": { "login": "ahuelsmann" },
                    "body": "Final review (P1, fresh session): no Blocker/High/Medium. Cross-family verification (P3, Gemini Argon): PASS, zero findings."
                  },
                  {
                    "author": { "login": "chatgpt-codex-connector" },
                    "body": "<!-- codex-review-state:v1 {\"code_review\":{\"has_any_findings\":false}} -->"
                  },
                  {
                    "author": { "login": "ahuelsmann" },
                    "body": "COO gate: reviews clean, arming auto-merge."
                  }
                ]
              }
            ]
            """;

        var failGitHub = false;
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("open"))
            {
                return failGitHub
                    ? new CommandResult(1, string.Empty, "network offline")
                    : new CommandResult(0, prJson, string.Empty);
            }

            if (fileName == "gh" && args.Contains("pr") && args.Contains("merged"))
            {
                return failGitHub
                    ? new CommandResult(1, string.Empty, "network offline")
                    : new CommandResult(0, mergedPrJson, string.Empty);
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
        Assert.Equal("OPEN", pr.State);
        Assert.Equal("Draft", pr.StateLabel);
        Assert.Equal("BEHIND", pr.MergeState);
        Assert.Equal("pass", pr.CiStatus);
        Assert.Equal("pass", pr.SonarStatus);
        Assert.Equal("L", pr.RiskClass);
        Assert.Equal([".github/instructions", "AGENTS.md"], pr.LockedAreas);
        Assert.Contains(pr.Participants, p => p.Role == "Writer" && p.Model == "GPT-6 Astra");

        var mergedPr = Assert.Single(first.RecentlyMerged);
        Assert.Equal(495, mergedPr.Number);
        Assert.Equal("MERGED", mergedPr.State);
        Assert.Equal("Gemergt", mergedPr.StateLabel);
        Assert.NotNull(mergedPr.MergedAtUtc);
        Assert.Contains(mergedPr.Participants, p => p.Role == "Final Reviewer" && p.Model == "Claude Opus 5.5" && p.Status == "PASS");
        Assert.Contains(mergedPr.Participants, p => p.Role == "Cross-Family Verifier" && p.Model == "Gemini 4 Argon" && p.Status == "PASS");

        // Second call: GitHub fails -> cached fallback with degraded status
        failGitHub = true;
        var second = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Equal("degraded", second.Source.Status);
        Assert.True(second.Source.FromCache);
        Assert.Single(second.PullRequests);
        Assert.Single(second.RecentlyMerged);
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
                "codex/strategy-mobaflow-kanban-board", "OPEN", "Draft", true, false,
                "BEHIND", "pass", "pass", "L", [], DateTimeOffset.UtcNow, null, null, [])
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
