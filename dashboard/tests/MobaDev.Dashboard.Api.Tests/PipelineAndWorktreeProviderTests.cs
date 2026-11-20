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

            if (fileName == "gh" && (args.Contains("project") || args.Contains("graphql")))
            {
                return new CommandResult(1, string.Empty, "missing required scopes [read:project]");
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
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
        Assert.Equal("Merged", mergedPr.StateLabel);
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
    public async Task PipelineProvider_ExtractsLinkedIssuesAndParenthesizedRiskClassAndIgnoresNone()
    {
        var issuesJson = """
            [
              {
                "number": 366,
                "title": "RF-09: Coverage and mutation ratchets",
                "url": "https://github.com/ahuelsmann/MOBAflow/issues/366",
                "state": "OPEN"
              },
              {
                "number": 451,
                "title": "RF-10: Formatting baseline and gate",
                "url": "https://github.com/ahuelsmann/MOBAflow/issues/451",
                "state": "OPEN"
              },
              {
                "number": 47,
                "title": "Quality and refactoring programme: RF-01 through RF-18",
                "url": "https://github.com/ahuelsmann/MOBAflow/issues/47",
                "state": "OPEN"
              }
            ]
            """;

        var openPrJson = """
            [
              {
                "number": 499,
                "title": "docs(format): final-newline rule and RF-10 Windows inventory (#451, slice 1)",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/499",
                "headRefName": "claude/rf10-rules",
                "isDraft": false,
                "mergeStateStatus": "CLEAN",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-19T14:00:00Z",
                "body": "## Slice header\n\n- Issue: #451\n- Risk class (H/M/L): M (formatting baseline)",
                "statusCheckRollup": [],
                "comments": [],
                "closingIssuesReferences": []
              },
              {
                "number": 486,
                "title": "RF-09 completion: further mutation lanes, last Safe-Mode sites and close-out",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/486",
                "headRefName": "claude/issue-366-rf09-completion",
                "isDraft": true,
                "mergeStateStatus": "BEHIND",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-18T21:00:00Z",
                "body": "## Related work\nRefs #366\nRefs #47",
                "statusCheckRollup": [],
                "comments": [],
                "closingIssuesReferences": []
              },
              {
                "number": 495,
                "title": "build(deps): bump Windows App SDK 2.7.0",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/495",
                "headRefName": "claude/deps-494",
                "isDraft": false,
                "mergeStateStatus": "CLEAN",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-19T12:00:00Z",
                "body": "## Slice header\n\n- Issue: none (Dependabot #494)\n- Risk class (H/M/L): M (Windows desktop runtime package)",
                "statusCheckRollup": [],
                "comments": [],
                "closingIssuesReferences": []
              }
            ]
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("open"))
            {
                return new CommandResult(0, openPrJson, string.Empty);
            }

            if (fileName == "gh" && args.Contains("pr") && args.Contains("merged"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, issuesJson, string.Empty);
            }

            if (fileName == "gh" && (args.Contains("project") || args.Contains("graphql")))
            {
                return new CommandResult(1, string.Empty, "missing scope");
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var provider = new PipelineProvider(runner);
        var result = await provider.GetPipelineAsync(new DashboardOptions(), CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.Equal(3, result.PullRequests.Count);

        var pr499 = Assert.Single(result.PullRequests, p => p.Number == 499);
        Assert.Equal("M", pr499.RiskClass);
        var issue451 = Assert.Single(pr499.LinkedIssues);
        Assert.Equal(451, issue451.Number);
        Assert.Equal("RF-10: Formatting baseline and gate", issue451.Title);
        Assert.Equal("https://github.com/ahuelsmann/MOBAflow/issues/451", issue451.Url);
        Assert.Equal("OPEN", issue451.State);

        var pr486 = Assert.Single(result.PullRequests, p => p.Number == 486);
        Assert.Equal(2, pr486.LinkedIssues.Count);
        Assert.Contains(pr486.LinkedIssues, i => i.Number == 366 && i.Title == "RF-09: Coverage and mutation ratchets");
        Assert.Contains(pr486.LinkedIssues, i => i.Number == 47 && i.Title == "Quality and refactoring programme: RF-01 through RF-18");

        var pr495 = Assert.Single(result.PullRequests, p => p.Number == 495);
        Assert.Equal("M", pr495.RiskClass);
        Assert.Empty(pr495.LinkedIssues);
    }

    [Fact]
    public async Task PipelineProvider_ExtractsBlockedReasonsAndInlineKanbanStatus()
    {
        var openPrJson = """
            [
              {
                "number": 501,
                "title": "ci(quality): enforce whitespace gate (RF-10 slice 3)",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/501",
                "headRefName": "claude/rf10-gate",
                "isDraft": true,
                "mergeStateStatus": "BLOCKED",
                "mergeable": "MERGEABLE",
                "reviewDecision": "REVIEW_REQUIRED",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-19T22:41:28Z",
                "body": "## Slice header\n\n- Issue: #451\n- Risk class (H/M/L): L",
                "statusCheckRollup": [
                  { "name": "Windows desktop build, tests, and coverage", "status": "IN_PROGRESS", "conclusion": "" },
                  { "name": "Backend mutation testing", "status": "IN_PROGRESS", "conclusion": "" },
                  { "name": "SharedUI mutation testing", "status": "QUEUED", "conclusion": "" }
                ],
                "comments": [],
                "closingIssuesReferences": [],
                "projectItems": [
                  { "title": "Kanban", "status": { "name": "In review" } }
                ]
              },
              {
                "number": 502,
                "title": "fix(ui): resolve merge conflict",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/502",
                "headRefName": "codex/ui-fix",
                "isDraft": false,
                "mergeStateStatus": "DIRTY",
                "mergeable": "CONFLICTING",
                "reviewDecision": "",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-19T22:42:00Z",
                "body": "## Slice header\n\n- Risk class (H/M/L): M",
                "statusCheckRollup": [
                  { "name": "SonarCloud Code Analysis", "status": "COMPLETED", "conclusion": "FAILURE" }
                ],
                "comments": [],
                "closingIssuesReferences": []
              }
            ]
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("open"))
            {
                return new CommandResult(0, openPrJson, string.Empty);
            }

            if (fileName == "gh" && args.Contains("pr") && args.Contains("merged"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && (args.Contains("project") || args.Contains("graphql")))
            {
                return new CommandResult(1, string.Empty, "missing scope");
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var provider = new PipelineProvider(runner);
        var result = await provider.GetPipelineAsync(new DashboardOptions(), CancellationToken.None);

        var pr501 = Assert.Single(result.PullRequests, p => p.Number == 501);
        Assert.Equal("In review", pr501.ProjectStatus);
        Assert.Contains("Draft PR (not yet marked Ready for review)", pr501.MergeBlockReasons);
        Assert.Contains("Required approving review missing", pr501.MergeBlockReasons);
        Assert.Contains(
            "3 checks running: Windows desktop build, tests, and coverage, Backend mutation testing (+1 more)",
            pr501.MergeBlockReasons);

        var pr502 = Assert.Single(result.PullRequests, p => p.Number == 502);
        Assert.Contains("Merge conflict with main — merge main and resolve conflicts", pr502.MergeBlockReasons);
        Assert.Contains("Check failing: SonarCloud Code Analysis", pr502.MergeBlockReasons);
    }

    [Fact]
    public async Task PipelineProvider_LoadsActiveKanbanIssuesEnrichesFromPrsAndDetectsBoardDrift()
    {
        var now = DateTimeOffset.UtcNow;
        var recentIso = now.AddHours(-2).ToString("O");
        var staleIso = now.AddDays(-10).ToString("O");
        var closedIso = now.AddDays(-15).ToString("O");

        var openPrJson = $$"""
            [
              {
                "number": 501,
                "title": "ci(quality): enforce whitespace gate (RF-10 slice 3)",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/501",
                "headRefName": "claude/rf10-gate",
                "isDraft": true,
                "mergeStateStatus": "BLOCKED",
                "mergeable": "MERGEABLE",
                "reviewDecision": "",
                "autoMergeRequest": null,
                "updatedAt": "{{recentIso}}",
                "body": "## Slice header\n\n- Issue: #451\n- Risk class (H/M/L): L\n- Locked areas: `quality.yml`, `CONTRIBUTING.md`\n- Writer: COO (Claude)",
                "statusCheckRollup": [],
                "comments": [],
                "closingIssuesReferences": [],
                "projectItems": []
              }
            ]
            """;

        var graphQlJson = $$"""
            {
              "data": {
                "user": {
                  "projectV2": {
                    "items": {
                      "nodes": [
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In progress", "field": { "name": "Status" } },
                              { "name": "P0", "field": { "name": "Priority" } },
                              { "name": "Infrastructure", "field": { "name": "Area" } },
                              { "name": "Refactoring", "field": { "name": "Type" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 47,
                            "title": "Quality and refactoring programme: RF-01 through RF-18",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/47",
                            "state": "OPEN",
                            "updatedAt": "{{recentIso}}"
                          }
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In progress", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 136,
                            "title": "Text-to-Speech interface modularization",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/136",
                            "state": "OPEN",
                            "updatedAt": "{{staleIso}}"
                          }
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In progress", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 266,
                            "title": "ci: keep documentation consistent within each pull request",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/266",
                            "state": "CLOSED",
                            "updatedAt": "{{closedIso}}"
                          }
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In progress", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 451,
                            "title": "RF-10: Formatting baseline and gate",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/451",
                            "state": "OPEN",
                            "updatedAt": "{{recentIso}}"
                          }
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In review", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "PullRequest",
                            "number": 501,
                            "title": "ci(quality): enforce whitespace gate (RF-10 slice 3)",
                            "url": "https://github.com/ahuelsmann/MOBAflow/pull/501",
                            "state": "OPEN",
                            "isDraft": true,
                            "updatedAt": "{{recentIso}}"
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("open"))
            {
                return new CommandResult(0, openPrJson, string.Empty);
            }

            if (fileName == "gh" && args.Contains("pr") && args.Contains("merged"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("graphql"))
            {
                return new CommandResult(0, graphQlJson, string.Empty);
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var provider = new PipelineProvider(runner);
        var result = await provider.GetPipelineAsync(new DashboardOptions(), CancellationToken.None);

        Assert.True(result.ProjectBoardAvailable);
        Assert.Equal("https://github.com/users/ahuelsmann/projects/1", result.ProjectBoardUrl);
        Assert.Equal("In review", Assert.Single(result.PullRequests, p => p.Number == 501).ProjectStatus);

        Assert.Equal([501, 451, 47, 136, 266], result.ActiveIssues.Select(i => i.Number).ToArray());

        var pr501Item = result.ActiveIssues[0];
        Assert.Equal("PullRequest", pr501Item.ItemKind);
        Assert.Equal("In review", pr501Item.KanbanStatus);
        Assert.Equal("L", pr501Item.RiskClass);
        Assert.Equal(["quality.yml", "CONTRIBUTING.md"], pr501Item.LockedAreas);
        Assert.Single(pr501Item.LinkedIssues, li => li.Number == 451);
        Assert.Null(pr501Item.BoardDriftReason);

        var issue451 = result.ActiveIssues[1];
        Assert.Equal("Issue", issue451.ItemKind);
        Assert.Equal("In progress", issue451.KanbanStatus);
        Assert.Equal("L", issue451.RiskClass);
        Assert.Equal(["quality.yml", "CONTRIBUTING.md"], issue451.LockedAreas);
        Assert.Single(issue451.LinkedPullRequests, p => p.Number == 501);
        Assert.Null(issue451.BoardDriftReason);

        var issue47 = result.ActiveIssues[2];
        Assert.Equal("Issue", issue47.ItemKind);
        Assert.Equal("P0", issue47.Priority);
        Assert.Equal("Infrastructure", issue47.Area);
        Assert.Empty(issue47.LinkedPullRequests);
        Assert.Null(issue47.BoardDriftReason);

        var issue136 = result.ActiveIssues[3];
        Assert.Equal("No active PR or Writer (stale on board)", issue136.BoardDriftReason);

        var issue266 = result.ActiveIssues[4];
        Assert.Equal("Closed on GitHub — move to Done on Kanban", issue266.BoardDriftReason);
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
                "BEHIND", "pass", "pass", "L", [], DateTimeOffset.UtcNow, null, null, [], [], [])
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

    [Fact]
    public async Task WorktreeProvider_GetBranchHygieneAsync_ClassifiesActiveAndStaleLocalAndRemoteBranches()
    {
        using var fx = new TempDashboardFixture();
        var flowLocalHeads = """
            main|
            claude/review-from-start|
            codex/strategy-mobaflow-kanban-board|
            antigravity/merged-old|[gone]
            """;
        var flowRemoteHeads = """
            origin/HEAD
            origin/main
            origin/codex/strategy-mobaflow-kanban-board
            origin/codex/stale-remote
            """;
        var devLocalHeads = """
            main|
            ui/old-dev-branch|[gone]
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName != "git" || !args.Contains("for-each-ref"))
            {
                return new CommandResult(1, string.Empty, "unexpected");
            }

            var repoArg = args.Count > 1 ? args[1] : string.Empty;
            var isRemote = args.Any(a => a.Contains("refs/remotes/origin", StringComparison.Ordinal));
            if (string.Equals(repoArg, fx.MobaFlowPath, StringComparison.OrdinalIgnoreCase))
            {
                return isRemote
                    ? new CommandResult(0, flowRemoteHeads, string.Empty)
                    : new CommandResult(0, flowLocalHeads, string.Empty);
            }

            if (string.Equals(repoArg, fx.MobaDevPath, StringComparison.OrdinalIgnoreCase))
            {
                return isRemote
                    ? new CommandResult(0, string.Empty, string.Empty)
                    : new CommandResult(0, devLocalHeads, string.Empty);
            }

            return new CommandResult(1, string.Empty, "unknown repo");
        });

        var provider = new WorktreeProvider(runner);
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var openPrs = new[]
        {
            new Contracts.PullRequestInfo(
                492, "docs", "https://github.com/ahuelsmann/MOBAflow/pull/492",
                "codex/strategy-mobaflow-kanban-board", "OPEN", "Draft", true, false,
                "BEHIND", "pass", "pass", "L", [], DateTimeOffset.UtcNow, null, null, [], [], [])
        };

        var (branches, staleLocalCount, staleRemoteCount) =
            await provider.GetBranchHygieneAsync(paths, openPrs, CancellationToken.None);

        Assert.Equal(2, staleLocalCount);
        Assert.Equal(1, staleRemoteCount);
        Assert.DoesNotContain(branches, b => b.Name == "main");

        var goneLocal = Assert.Single(branches, b => b.Repository == "MOBAflow" && b.Name == "antigravity/merged-old");
        Assert.Equal("stale", goneLocal.Status);
        Assert.True(goneLocal.UpstreamGone);

        var devStale = Assert.Single(branches, b => b.Repository == "MOBAdev" && b.Name == "ui/old-dev-branch");
        Assert.Equal("stale", devStale.Status);
        Assert.True(devStale.UpstreamGone);

        var remoteStale = Assert.Single(branches, b => b.Repository == "MOBAflow" && b.Scope == "remote" && b.Name == "codex/stale-remote");
        Assert.Equal("stale", remoteStale.Status);

        var standing = Assert.Single(branches, b => b.Repository == "MOBAflow" && b.Name == "claude/review-from-start");
        Assert.Equal("active", standing.Status);

        var activePrLocal = Assert.Single(branches, b => b.Repository == "MOBAflow" && b.Scope == "local" && b.Name == "codex/strategy-mobaflow-kanban-board");
        Assert.Equal("active", activePrLocal.Status);
        Assert.Equal(492, activePrLocal.LinkedPrNumber);

        // Stale branches appear first in sorted list
        Assert.Equal("stale", branches[0].Status);
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
