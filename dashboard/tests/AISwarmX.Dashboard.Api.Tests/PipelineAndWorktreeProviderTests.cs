using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Providers;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

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
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In review", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "PullRequest",
                            "number": 502,
                            "title": "docs: stale merged pr still in review",
                            "url": "https://github.com/ahuelsmann/MOBAflow/pull/502",
                            "state": "MERGED",
                            "isDraft": false,
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

        Assert.Equal([451, 47, 136, 266, 502], result.ActiveIssues.Select(i => i.Number).ToArray());
        Assert.DoesNotContain(result.ActiveIssues, i => i.Number == 501);

        var issue451 = result.ActiveIssues[0];
        Assert.Equal("Issue", issue451.ItemKind);
        Assert.Equal("In progress", issue451.KanbanStatus);
        Assert.Equal("L", issue451.RiskClass);
        Assert.Equal(["quality.yml", "CONTRIBUTING.md"], issue451.LockedAreas);
        Assert.Single(issue451.LinkedPullRequests, p => p.Number == 501);
        Assert.Null(issue451.BoardDriftReason);

        var issue47 = result.ActiveIssues[1];
        Assert.Equal("Issue", issue47.ItemKind);
        Assert.Equal("P0", issue47.Priority);
        Assert.Equal("Infrastructure", issue47.Area);
        Assert.Empty(issue47.LinkedPullRequests);
        Assert.Null(issue47.BoardDriftReason);

        var issue136 = result.ActiveIssues[2];
        Assert.Equal("No active PR or Writer (stale on board)", issue136.BoardDriftReason);

        var issue266 = result.ActiveIssues[3];
        Assert.Equal("Closed on GitHub — move to Done on Kanban", issue266.BoardDriftReason);

        var pr502Item = result.ActiveIssues[4];
        Assert.Equal("PullRequest", pr502Item.ItemKind);
        Assert.Equal("In review", pr502Item.KanbanStatus);
        Assert.Equal("Merged/Closed on GitHub — move to Done on Kanban", pr502Item.BoardDriftReason);
    }

    [Fact]
    public async Task PipelineProvider_OrdersKanbanWorkflowReadyInProgressInReviewAndSynthesizesKanbanWriter()
    {
        var nowIso = DateTimeOffset.UtcNow.ToString("O");
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
                              { "name": "In review", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 510,
                            "title": "docs: review item",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/510",
                            "state": "OPEN",
                            "updatedAt": "{{nowIso}}"
                          }
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "In progress", "field": { "name": "Status" } },
                              { "name": "Antigravity (P4 Hydrogen)", "field": { "name": "Writer" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 430,
                            "title": "MOBAflow Web Demo",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/430",
                            "state": "OPEN",
                            "updatedAt": "{{nowIso}}"
                          }
                        },
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "Ready", "field": { "name": "Status" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 431,
                            "title": "Ready issue without writer",
                            "url": "https://github.com/ahuelsmann/MOBAflow/issues/431",
                            "state": "OPEN",
                            "updatedAt": "{{nowIso}}"
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
                return new CommandResult(0, "[]", string.Empty);
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

        Assert.Equal([431, 430, 510], result.ActiveIssues.Select(i => i.Number).ToArray());

        var issue430 = result.ActiveIssues[1];
        var writer = Assert.Single(issue430.Participants);
        Assert.Equal("Writer", writer.Role);
        Assert.Equal("P4", writer.Profile);
        Assert.Equal("Antigravity", writer.Tool);
        Assert.Equal("Gemini 4 Hydrogen", writer.Model);
        Assert.Equal("Active", writer.Status);
        Assert.Equal("Kanban Writer", writer.EvidenceSource);

        var issue431 = result.ActiveIssues[0];
        Assert.Empty(issue431.Participants);
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

        var devStale = Assert.Single(branches, b => b.Repository == "AISwarmX" && b.Name == "ui/old-dev-branch");
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

    [Fact]
    public async Task WorktreeProvider_DetectsDirtyPreservedWorktreeAndExcludesFromStaleCount()
    {
        using var fx = new TempDashboardFixture();
        var cleanWtPath = Path.Combine(fx.MobaFlowPath, "artifacts", "worktrees", "clean-old");
        var dirtyWtPath = Path.Combine(fx.MobaFlowPath, "artifacts", "worktrees", "issue-398-dirty");
        Directory.CreateDirectory(cleanWtPath);
        Directory.CreateDirectory(dirtyWtPath);

        var porcelain = $"""
            worktree {fx.MobaFlowPath.Replace('\\', '/')}
            HEAD 72a78bd01ae219358776e665444550a1353ea2b2
            branch refs/heads/main

            worktree {dirtyWtPath.Replace('\\', '/')}
            HEAD b45e901211111111111111111111111111111111
            branch refs/heads/codex/issue-398-dirty

            worktree {cleanWtPath.Replace('\\', '/')}
            HEAD a12e606311111111111111111111111111111111
            branch refs/heads/codex/clean-old
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("open"))
            {
                return new CommandResult(0, "[]", string.Empty);
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

            if (fileName == "git" && args.Contains("worktree"))
            {
                return new CommandResult(0, porcelain, string.Empty);
            }

            if (fileName == "git" && args.Contains("for-each-ref"))
            {
                return new CommandResult(0, string.Empty, string.Empty);
            }

            if (fileName == "git" && args.Contains("status") && args.Contains("--porcelain"))
            {
                var targetDir = args.Count > 1 ? args[1] : string.Empty;
                var normalizedTarget = targetDir.Replace('\\', '/');
                if (string.Equals(normalizedTarget, dirtyWtPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    return new CommandResult(0, " M src/DirtyFile.cs\n?? src/NewSketch.cs\n", string.Empty);
                }

                return new CommandResult(0, string.Empty, string.Empty);
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var options = new DashboardOptions
        {
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            SwarmBusDbPath = Path.Combine(fx.RootPath, "swarm-bus.db")
        };
        var provider = new WorktreeProvider(runner);
        var paths = options.ResolvePaths();
        var (worktrees, _) = await provider.GetWorktreesAsync(paths, [], CancellationToken.None);

        Assert.Equal(3, worktrees.Count);
        Assert.Equal("codex/clean-old", worktrees[0].Branch);
        Assert.False(worktrees[0].IsDirty);
        Assert.Equal("codex/issue-398-dirty", worktrees[1].Branch);
        Assert.True(worktrees[1].IsDirty);
        Assert.Equal("main", worktrees[2].Branch);

        var snapshotService = new AISwarmX.Dashboard.Api.Services.DashboardSnapshotService(
            new StaticOptionsMonitor<DashboardOptions>(options),
            new ClaudeMonitorProvider(),
            new CodexMonitorProvider(),
            new AntigravityMonitorProvider(),
            new GovernanceProvider(),
            new ConfigDocsProvider(),
            new PipelineProvider(runner),
            provider,
            new SwarmBusProvider());

        var snapshot = await snapshotService.GetSnapshotAsync(forceRefresh: true, CancellationToken.None);
        Assert.Equal(1, snapshot.Hygiene.StaleWorktreeCount);
        Assert.Equal(1, snapshot.WarningCount);
    }

    [Theory]
    [InlineData("Pull requests are disabled for this repository.", null)]
    [InlineData("HTTP 410: Gone (https://api.github.com/repos/ahuelsmann/MOBAdev/pulls)", null)]
    [InlineData("HTTP 404: Not Found (https://api.github.com/repos/ahuelsmann/MOBAdev/pulls)", "false")]
    public async Task PipelineProvider_ReturnsOkWithActiveIssuesWhenPullRequestsAreDisabled(
        string prDisabledStdErr,
        string? metadataHasPullRequests)
    {
        var graphQlJson = """
            {
              "data": {
                "user": {
                  "projectV2": {
                    "items": {
                      "nodes": [
                        {
                          "fieldValues": {
                            "nodes": [
                              { "name": "Ready", "field": { "name": "Status" } },
                              { "name": "M", "field": { "name": "Risk class" } }
                            ]
                          },
                          "content": {
                            "__typename": "Issue",
                            "number": 57,
                            "title": "Workspace configuration contract",
                            "url": "https://github.com/ahuelsmann/MOBAdev/issues/57",
                            "state": "OPEN",
                            "updatedAt": "2026-11-27T09:00:00Z"
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
            if (fileName == "gh" && args.Contains("pr"))
            {
                return new CommandResult(1, string.Empty, prDisabledStdErr);
            }

            if (fileName == "gh" && args.Contains("api") && args.Contains("repos/ahuelsmann/MOBAdev"))
            {
                return metadataHasPullRequests is null
                    ? new CommandResult(1, string.Empty, "unexpected metadata query")
                    : new CommandResult(0, metadataHasPullRequests, string.Empty);
            }

            if (fileName == "gh" && args.Contains("graphql"))
            {
                return new CommandResult(0, graphQlJson, string.Empty);
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var options = new DashboardOptions
        {
            GitHubRepository = "ahuelsmann/MOBAdev",
            GitHubProjectOwner = "ahuelsmann",
            GitHubProjectNumber = 1
        };

        var provider = new PipelineProvider(runner);
        var result = await provider.GetPipelineAsync(options, CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.Contains("Pull requests are disabled", result.Source.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.PullRequests);
        Assert.Empty(result.RecentlyMerged);
        Assert.True(result.ProjectBoardAvailable);
        var issue = Assert.Single(result.ActiveIssues);
        Assert.Equal(57, issue.Number);
        Assert.Equal("Ready", issue.KanbanStatus);
    }

    [Theory]
    [InlineData(0, "true")]
    [InlineData(1, "HTTP 404: Not Found")]
    public async Task PipelineProvider_TreatsAmbiguous404OnPullsAsUnavailableWhenMetadataDoesNotConfirmDisabled(
        int metadataExitCode,
        string metadataOutput)
    {
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr"))
            {
                return new CommandResult(1, string.Empty, "HTTP 404: Not Found (https://api.github.com/repos/ahuelsmann/typo-repo/pulls)");
            }

            if (fileName == "gh" && args.Contains("api") && args.Contains("repos/ahuelsmann/typo-repo"))
            {
                return metadataExitCode == 0
                    ? new CommandResult(0, metadataOutput, string.Empty)
                    : new CommandResult(metadataExitCode, string.Empty, metadataOutput);
            }

            if (fileName == "gh" && (args.Contains("graphql") || args.Contains("issue")))
            {
                return new CommandResult(1, string.Empty, "not found");
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var options = new DashboardOptions
        {
            GitHubRepository = "ahuelsmann/typo-repo",
            GitHubProjectOwner = "ahuelsmann",
            GitHubProjectNumber = 1
        };

        var provider = new PipelineProvider(runner);
        var result = await provider.GetPipelineAsync(options, CancellationToken.None);

        Assert.Equal("unavailable", result.Source.Status);
        Assert.Equal("ahuelsmann/typo-repo", result.Repository);
        Assert.Empty(result.PullRequests);
        Assert.Empty(result.ActiveIssues);
        Assert.False(result.ProjectBoardAvailable);
    }

    [Fact]
    public async Task PipelineProvider_AndSnapshotService_IsolateCachesAcrossWorkspaceSwitches()
    {
        using var fx = new TempDashboardFixture();
        var workspaceA = new DashboardOptions
        {
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaFlowPath,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            SwarmBusDbPath = Path.Combine(fx.RootPath, "swarm-bus.db"),
            GitHubRepository = "ahuelsmann/MOBAflow",
            GitHubProjectOwner = "ahuelsmann",
            GitHubProjectNumber = 1,
            LocalCacheTtlSeconds = 60,
            GitHubCacheTtlSeconds = 60
        };

        var workspaceB = new DashboardOptions
        {
            MobaDevRepoPath = fx.MobaDevPath,
            MobaFlowRepoPath = fx.MobaDevPath,
            ClaudeHomePath = fx.ClaudeHomePath,
            CodexHomePath = fx.CodexHomePath,
            AntigravityHomePath = fx.AntigravityHomePath,
            GeminiConfigPath = fx.GeminiConfigPath,
            SwarmBusDbPath = Path.Combine(fx.RootPath, "swarm-bus.db"),
            GitHubRepository = "ahuelsmann/MOBAdev",
            GitHubProjectOwner = "ahuelsmann",
            GitHubProjectNumber = 2,
            LocalCacheTtlSeconds = 60,
            GitHubCacheTtlSeconds = 60
        };

        var flowPrJson = """
            [
              {
                "number": 492,
                "title": "feat: flow PR",
                "url": "https://github.com/ahuelsmann/MOBAflow/pull/492",
                "headRefName": "codex/flow-pr",
                "isDraft": false,
                "mergeStateStatus": "CLEAN",
                "autoMergeRequest": null,
                "updatedAt": "2026-11-27T08:00:00Z",
                "body": "",
                "statusCheckRollup": []
              }
            ]
            """;

        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("ahuelsmann/MOBAflow"))
            {
                return args.Contains("open")
                    ? new CommandResult(0, flowPrJson, string.Empty)
                    : new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("pr") && args.Contains("ahuelsmann/MOBAdev"))
            {
                return new CommandResult(1, string.Empty, "network offline");
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("graphql"))
            {
                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "git")
            {
                return new CommandResult(0, string.Empty, string.Empty);
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var optionsMonitor = new MutableOptionsMonitor<DashboardOptions>(workspaceA);
        var snapshotService = new AISwarmX.Dashboard.Api.Services.DashboardSnapshotService(
            optionsMonitor,
            new ClaudeMonitorProvider(),
            new CodexMonitorProvider(),
            new AntigravityMonitorProvider(),
            new GovernanceProvider(),
            new ConfigDocsProvider(),
            new PipelineProvider(runner),
            new WorktreeProvider(runner),
            new SwarmBusProvider());

        var firstSnapshot = await snapshotService.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);
        Assert.Equal("ahuelsmann/MOBAflow", firstSnapshot.Pipeline.Repository);
        Assert.Equal("ok", firstSnapshot.Pipeline.Source.Status);
        Assert.Single(firstSnapshot.Pipeline.PullRequests);

        // Switch workspace dynamically while forceRefresh is false and GitHub fails for the new workspace.
        optionsMonitor.Set(workspaceB);
        var switchedSnapshot = await snapshotService.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);

        Assert.Equal("ahuelsmann/MOBAdev", switchedSnapshot.Pipeline.Repository);
        Assert.Equal("unavailable", switchedSnapshot.Pipeline.Source.Status);
        Assert.False(switchedSnapshot.Pipeline.Source.FromCache);
        Assert.Empty(switchedSnapshot.Pipeline.PullRequests);
    }

    [Fact]
    public async Task WorktreeProvider_WhenWorkspaceMatchesInstallationPath_ScansOnceAsAISwarmXWithoutDuplicates()
    {
        var tempRoot = Directory.CreateTempSubdirectory("aiswarmx-same-repo-");
        try
        {
            var gitCalls = 0;
            var runner = new FakeCommandRunner((_, args) =>
            {
                Interlocked.Increment(ref gitCalls);
                if (args.Any(a => a.Contains("refs/heads", StringComparison.Ordinal)))
                {
                    return new CommandResult(0, "main|\nfeature/stale|[gone]", string.Empty);
                }

                if (args.Any(a => a.Contains("refs/remotes/origin", StringComparison.Ordinal)))
                {
                    return new CommandResult(0, "origin/HEAD\norigin/main\norigin/feature/old", string.Empty);
                }

                return new CommandResult(0, string.Empty, string.Empty);
            });

            var provider = new WorktreeProvider(runner);
            var paths = new DashboardOptions
            {
                MobaFlowRepoPath = tempRoot.FullName,
                MobaDevRepoPath = tempRoot.FullName + Path.DirectorySeparatorChar
            }.ResolvePaths();

            var (branches, staleLocalCount, staleRemoteCount) =
                await provider.GetBranchHygieneAsync(paths, [], CancellationToken.None);

            Assert.Equal(2, gitCalls);
            Assert.Equal(1, staleLocalCount);
            Assert.Equal(1, staleRemoteCount);
            Assert.Equal(2, branches.Count);
            Assert.All(branches, b => Assert.Equal("AISwarmX", b.Repository));
            Assert.Single(branches, b => b.Scope == "local" && b.Name == "feature/stale");
            Assert.Single(branches, b => b.Scope == "remote" && b.Name == "feature/old");
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PipelineProvider_LoadsWorkflowRunsAcrossMonitoredRepositoriesAndSortsActiveFirst()
    {
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("pr") && args.Contains("open"))
            {
                return new CommandResult(0, "[]", string.Empty);
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
                return new CommandResult(1, string.Empty, "missing scope");
            }

            if (fileName == "gh" && args.Contains("run"))
            {
                var repoIndex = args.ToList().IndexOf("--repo");
                var repo = repoIndex >= 0 && repoIndex + 1 < args.Count ? args[repoIndex + 1] : string.Empty;

                if (repo == "ahuelsmann/MOBAflow")
                {
                    return new CommandResult(0, """
                        [
                          {
                            "databaseId": 1001,
                            "workflowName": "Windows CI",
                            "displayTitle": "fix: a11y trackplan",
                            "headBranch": "main",
                            "status": "completed",
                            "conclusion": "success",
                            "url": "https://github.com/ahuelsmann/MOBAflow/actions/runs/1001",
                            "createdAt": "2026-11-27T21:50:00Z",
                            "updatedAt": "2026-11-27T21:55:00Z"
                          }
                        ]
                        """, string.Empty);
                }

                if (repo == "ahuelsmann/SentiX")
                {
                    return new CommandResult(0, """
                        [
                          {
                            "databaseId": 2002,
                            "workflowName": "SentiX Linux CI",
                            "displayTitle": "docs(specs): phase 3",
                            "headBranch": "feat/phase3-shadow-dashboard",
                            "status": "in_progress",
                            "conclusion": null,
                            "url": "https://github.com/ahuelsmann/SentiX/actions/runs/2002",
                            "createdAt": "2026-11-27T21:58:00Z",
                            "updatedAt": "2026-11-27T21:59:00Z"
                          }
                        ]
                        """, string.Empty);
                }

                if (repo == "ahuelsmann/AISwarmX")
                {
                    return new CommandResult(1, string.Empty, "rate limited for single repo");
                }
            }

            return new CommandResult(1, string.Empty, "unexpected");
        });

        var options = new DashboardOptions
        {
            GitHubRepository = "ahuelsmann/MOBAflow",
            GitHubProjectNumber = 0
        };

        var provider = new PipelineProvider(runner);
        var result = await provider.GetPipelineAsync(options, CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.NotNull(result.WorkflowRuns);
        Assert.Equal(2, result.WorkflowRuns!.Count);
        Assert.Equal("ahuelsmann/SentiX", result.WorkflowRuns[0].Repository);
        Assert.Equal("in_progress", result.WorkflowRuns[0].Status);
        Assert.Equal(2002, result.WorkflowRuns[0].DatabaseId);
        Assert.Equal("ahuelsmann/MOBAflow", result.WorkflowRuns[1].Repository);
        Assert.Equal("success", result.WorkflowRuns[1].Conclusion);
    }

    [Fact]
    public void PipelineProvider_ValidatesSlugsResolvesMonitoredReposAndPreventsCacheCollisions()
    {
        Assert.True(PipelineProvider.IsValidGitHubRepositorySlug("ahuelsmann/MOBAflow"));
        Assert.True(PipelineProvider.IsValidGitHubRepositorySlug("my-org-1/repo.name-2"));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("my_org/repo"));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("-x/y"));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("a/b/c"));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("/x"));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("./.."));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("../x"));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("owner/."));
        Assert.False(PipelineProvider.IsValidGitHubRepositorySlug("owner/.."));

        var defaultRepos = new DashboardOptions { GitHubRepository = "ahuelsmann/MOBAflow" }.ResolveMonitoredRepositories();
        Assert.Equal(["ahuelsmann/MOBAflow", "ahuelsmann/SentiX", "ahuelsmann/AISwarmX"], defaultRepos);

        var dedupedRepos = new DashboardOptions
        {
            MonitoredRepositories = ["ahuelsmann/SentiX", "AHUELSMANN/sentix", " ahuelsmann/MOBAflow "]
        }.ResolveMonitoredRepositories();
        Assert.Equal(["ahuelsmann/SentiX", "ahuelsmann/MOBAflow"], dedupedRepos);

        var keyInvalidComma = PipelineProvider.BuildCacheKey(new DashboardOptions { MonitoredRepositories = ["a/b,c/d"] });
        var keyValidPair = PipelineProvider.BuildCacheKey(new DashboardOptions { MonitoredRepositories = ["a/b", "c/d"] });
        Assert.NotEqual(keyInvalidComma, keyValidPair);
    }

    [Fact]
    public async Task PipelineProvider_PreservesWorkflowRunsWhenPrLookupFails()
    {
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("run"))
            {
                return new CommandResult(0, """
                    [
                      {
                        "databaseId": 3003,
                        "workflowName": "SentiX Linux CI",
                        "displayTitle": "phase 3 check",
                        "headBranch": "main",
                        "status": "in_progress",
                        "conclusion": null,
                        "url": "https://github.com/ahuelsmann/SentiX/actions/runs/3003",
                        "createdAt": "2026-11-27T22:00:00Z",
                        "updatedAt": "2026-11-27T22:01:00Z"
                      }
                    ]
                    """, string.Empty);
            }

            return new CommandResult(1, string.Empty, "pr api error");
        });

        var options = new DashboardOptions
        {
            GitHubRepository = "ahuelsmann/MOBAflow",
            GitHubProjectNumber = 0,
            MonitoredRepositories = ["ahuelsmann/SentiX"]
        };

        var result = await new PipelineProvider(runner).GetPipelineAsync(options, CancellationToken.None);

        Assert.Equal("unavailable", result.Source.Status);
        Assert.NotNull(result.WorkflowRuns);
        var run = Assert.Single(result.WorkflowRuns!);
        Assert.Equal(3003, run.DatabaseId);
        Assert.Equal("in_progress", run.Status);
    }

    [Fact]
    public async Task PipelineProvider_AuthoritativeEmptyWorkflowRunsClearsCachedQueuedRun()
    {
        var runsJson = """
            [
              {
                "databaseId": 4004,
                "workflowName": "SentiX Linux CI",
                "displayTitle": "queued run",
                "headBranch": "main",
                "status": "queued",
                "conclusion": null,
                "url": "https://github.com/ahuelsmann/SentiX/actions/runs/4004",
                "createdAt": "2026-11-27T22:00:00Z",
                "updatedAt": "2026-11-27T22:01:00Z"
              }
            ]
            """;
        var prFailing = false;
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("run"))
            {
                return new CommandResult(0, runsJson, string.Empty);
            }

            if (prFailing)
            {
                return new CommandResult(1, string.Empty, "pr error");
            }

            if (fileName == "gh" && args.Contains("pr"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            if (fileName == "gh" && args.Contains("issue"))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            return new CommandResult(1, string.Empty, "skip");
        });

        var options = new DashboardOptions
        {
            GitHubRepository = "ahuelsmann/MOBAflow",
            GitHubProjectNumber = 0,
            MonitoredRepositories = ["ahuelsmann/SentiX"]
        };

        var provider = new PipelineProvider(runner);
        var initial = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Single(initial.WorkflowRuns!);

        // Now queued run was deleted upstream (gh run list succeeds returning []) AND PR lookup fails
        runsJson = "[]";
        prFailing = true;
        var updated = await provider.GetPipelineAsync(options, CancellationToken.None);

        Assert.Equal("degraded", updated.Source.Status);
        Assert.Equal("ok", updated.WorkflowRunsSource?.Status);
        Assert.NotNull(updated.WorkflowRuns);
        Assert.Empty(updated.WorkflowRuns!);
    }

    [Fact]
    public async Task PipelineProvider_UpdatesWorkflowCacheIndependentlyWhenPrFailsAndFlagsStaleWhenCiFails()
    {
        var runsJson = """
            [
              {
                "databaseId": 5005,
                "workflowName": "SentiX Linux CI",
                "displayTitle": "queued run",
                "headBranch": "main",
                "status": "queued",
                "conclusion": null,
                "url": "https://github.com/ahuelsmann/SentiX/actions/runs/5005",
                "createdAt": "2026-11-27T22:00:00Z",
                "updatedAt": "2026-11-27T22:01:00Z"
              }
            ]
            """;
        var prFailing = false;
        var ciFailing = false;
        var runner = new FakeCommandRunner((fileName, args) =>
        {
            if (fileName == "gh" && args.Contains("run"))
            {
                return ciFailing
                    ? new CommandResult(1, string.Empty, "actions outage")
                    : new CommandResult(0, runsJson, string.Empty);
            }

            if (prFailing)
            {
                return new CommandResult(1, string.Empty, "pr outage");
            }

            if (fileName == "gh" && (args.Contains("pr") || args.Contains("issue")))
            {
                return new CommandResult(0, "[]", string.Empty);
            }

            return new CommandResult(1, string.Empty, "skip");
        });

        var options = new DashboardOptions
        {
            GitHubRepository = "ahuelsmann/MOBAflow",
            GitHubProjectNumber = 0,
            MonitoredRepositories = ["ahuelsmann/SentiX"]
        };

        var provider = new PipelineProvider(runner);
        var seeded = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Single(seeded.WorkflowRuns!);
        Assert.Equal("ok", seeded.WorkflowRunsSource?.Status);
        Assert.False(seeded.WorkflowRunsSource?.FromCache);

        // PR succeeds + CI fails -> Source="ok", but cached WorkflowRuns flagged degraded + FromCache=true
        ciFailing = true;
        var staleCiOnly = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Equal("ok", staleCiOnly.Source.Status);
        Assert.Single(staleCiOnly.WorkflowRuns!);
        Assert.Equal("degraded", staleCiOnly.WorkflowRunsSource?.Status);
        Assert.True(staleCiOnly.WorkflowRunsSource?.FromCache);

        // PR fails + CI authoritatively returns [] -> clears independent workflow cache
        ciFailing = false;
        prFailing = true;
        runsJson = "[]";
        var cleared = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Equal("degraded", cleared.Source.Status);
        Assert.Equal("ok", cleared.WorkflowRunsSource?.Status);
        Assert.Empty(cleared.WorkflowRuns!);

        // Next refresh BOTH PR and CI fail -> old queued run MUST NOT reappear
        ciFailing = true;
        var bothFailing = await provider.GetPipelineAsync(options, CancellationToken.None);
        Assert.Equal("degraded", bothFailing.Source.Status);
        Assert.NotNull(bothFailing.WorkflowRuns);
        Assert.Empty(bothFailing.WorkflowRuns!);
    }

    [Fact]
    public void SwarmLiveBroadcastService_ChangeKeyDetectsNonFirstWorkflowTransitionAndMonitorUpdates()
    {
        var now = DateTimeOffset.UtcNow;
        static AISwarmX.Dashboard.Api.Contracts.DashboardSnapshot MakeSnapshot(
            DateTimeOffset ts,
            string secondRunStatus,
            string claudeState) => new(
            GeneratedAtUtc: ts,
            ReadOnly: true,
            BindAddress: "127.0.0.1:5179",
            OverallStatus: "ok",
            WarningCount: 0,
            Monitors: new AISwarmX.Dashboard.Api.Contracts.MonitorsResponse(ts, [
                new AISwarmX.Dashboard.Api.Contracts.MonitorInfo(
                    "claude-monitor", "AiSwarmX Claude Opus", "COO", "P1", "Claude Code",
                    claudeState, "Claude Opus 5.5", ts, null, 1, null,
                    new AISwarmX.Dashboard.Api.Contracts.SourceHealth("ok", ts), 0, null)
            ], new AISwarmX.Dashboard.Api.Contracts.SourceHealth("ok", ts)),
            Governance: new AISwarmX.Dashboard.Api.Contracts.GovernanceResponse(
                ts, [], [], [], [], [], 0, 0, 0,
                new AISwarmX.Dashboard.Api.Contracts.SourceHealth("ok", ts)),
            Pipeline: new AISwarmX.Dashboard.Api.Contracts.PipelineResponse(
                ts, "ahuelsmann/MOBAflow", [], [], [], false, null, null,
                new AISwarmX.Dashboard.Api.Contracts.SourceHealth("ok", ts),
                WorkflowRuns: [
                    new AISwarmX.Dashboard.Api.Contracts.WorkflowRunInfo(1, "ahuelsmann/MOBAflow", "Win CI", "a", "main", "in_progress", null, "https://github.com/ahuelsmann/MOBAflow/actions/runs/1", ts, ts),
                    new AISwarmX.Dashboard.Api.Contracts.WorkflowRunInfo(2, "ahuelsmann/SentiX", "Linux CI", "b", "main", secondRunStatus, null, "https://github.com/ahuelsmann/SentiX/actions/runs/2", ts, ts)
                ]),
            Hygiene: new AISwarmX.Dashboard.Api.Contracts.HygieneResponse(
                ts, [], [], 0, new AISwarmX.Dashboard.Api.Contracts.SourceHealth("ok", ts)));

        var keyQueued = AISwarmX.Dashboard.Api.Services.SwarmLiveBroadcastService.BuildSnapshotChangeKey(
            MakeSnapshot(now, "queued", "standby"));
        var keyInProgress = AISwarmX.Dashboard.Api.Services.SwarmLiveBroadcastService.BuildSnapshotChangeKey(
            MakeSnapshot(now, "in_progress", "standby"));
        var keyBusy = AISwarmX.Dashboard.Api.Services.SwarmLiveBroadcastService.BuildSnapshotChangeKey(
            MakeSnapshot(now, "in_progress", "busy"));

        Assert.NotEqual(keyQueued, keyInProgress);
        Assert.NotEqual(keyInProgress, keyBusy);
    }

    private sealed class MutableOptionsMonitor<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        private T _current = value;
        public T CurrentValue => _current;
        public T Get(string? name) => _current;
        public void Set(T next) => _current = next;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
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
