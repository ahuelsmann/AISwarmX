using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Providers;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed class GovernanceProviderTests
{
    [Fact]
    public async Task GetGovernanceAsync_ParsesTablesDetectsDriftAndNormalizesRepoRootPath()
    {
        using var fx = new TempDashboardFixture();

        // Populate synthetic MOBAflow docs
        var docsDir = Path.Combine(fx.MobaFlowPath, "docs");
        var instDir = Path.Combine(fx.MobaFlowPath, ".github", "instructions");
        Directory.CreateDirectory(docsDir);
        Directory.CreateDirectory(instDir);

        await File.WriteAllTextAsync(Path.Combine(docsDir, "AI-DEVELOPMENT.md"), """
            # AI-assisted development

            ## Capability profiles and models

            | Profile | Active models and clients | Strengths and typical assignments |
            | --- | --- | --- |
            | `P1` Depth, Architecture & Safety | **Claude Opus 5.5** | Deep architecture and safety gate |
            | `P2` Repo, Build & Fact Precision | **GPT-6 Astra** | Solution/build graph and CI verification |
            | `P3` System Context, Product & UI/UX | **Gemini 4 Argon** | WinUI 3 and MAUI UX |
            | `P4` Cadence, Triage & Fast Delivery | **Claude Fable 5.2**, **Gemini 4 Hydrogen** | Fast routine slices |
            """);

        await File.WriteAllTextAsync(Path.Combine(instDir, "master-coordination.instructions.md"), """
            # Master coordination

            ## Capability profiles and roles

            | Profile | Core strength | Typical duties |
            | --- | --- | --- |
            | `P1` Depth | Concurrency | Safety gate |

            | Role | Primary profile(s) | Responsibility |
            | --- | --- | --- |
            | Chief Operating Officer (COO) | `P1` (Claude) | Overall operational lead |
            | Technical Director (TD) | `P2` (Codex) | Backlog and build graph |

            ## Risk classes and domain matrix

            | Class | MOBAflow areas | Required team and review diversity |
            | --- | --- | --- |
            | `H` High | `Backend`, `Common` | Writer (`P1`/`P2`), companion + final `P1` + `P2` |
            | `M` Medium | `MOBAflow`, `SharedUI` | Writer + companion + `P3` UI/UX |
            | `L` Low | `docs/`, `specs/` | Final `P1` + cross-family check |
            """);

        // Populate MOBAdev vs local configs (1 synced with repo-root replacement, 1 drift, 1 missing_local, 1 hidden .sonar ignored)
        var repoRulesDir = Path.Combine(fx.MobaDevPath, "agents", "antigravity", "rules");
        var localRulesDir = Path.Combine(fx.GeminiConfigPath, "rules");
        var repoAgyDir = Path.Combine(fx.MobaDevPath, "agents", "antigravity", "agents");
        var localAgyDir = Path.Combine(fx.GeminiConfigPath, "agents");
        var repoClaudeDir = Path.Combine(fx.MobaDevPath, "agents", "claude", "agents");
        var localClaudeDir = Path.Combine(fx.ClaudeHomePath, "agents");

        foreach (var dir in new[] { repoRulesDir, localRulesDir, repoAgyDir, localAgyDir, repoClaudeDir, localClaudeDir })
        {
            Directory.CreateDirectory(dir);
        }

        // Hidden .sonar dir must be ignored
        Directory.CreateDirectory(Path.Combine(localRulesDir, ".sonar"));

        // Synced (only differs by <repo-root>/MOBAflow placeholder vs actual path and CRLF/LF)
        await File.WriteAllTextAsync(
            Path.Combine(repoRulesDir, "mobaflow-agent-network.md"),
            "Ecosystem (`<repo-root>/MOBAflow` and `ahuelsmann/MOBAdev`)\n");
        await File.WriteAllTextAsync(
            Path.Combine(localRulesDir, "mobaflow-agent-network.md"),
            $"Ecosystem (`{fx.MobaFlowPath}` and `ahuelsmann/MOBAdev`)\r\n");

        // Drift
        await File.WriteAllTextAsync(
            Path.Combine(repoAgyDir, "head-of-ai-strategy.md"),
            "---\nname: head-of-ai-strategy\ndescription: Strategic lead\n---\nLine A\n");
        await File.WriteAllTextAsync(
            Path.Combine(localAgyDir, "head-of-ai-strategy.md"),
            "---\nname: head-of-ai-strategy\ndescription: Strategic lead\n---\nLine A\nLine B modified\n");

        // Missing locally
        await File.WriteAllTextAsync(
            Path.Combine(repoClaudeDir, "p1-final-reviewer.md"),
            "---\nname: p1-final-reviewer\ndescription: Fresh P1 reviewer\n---\n");

        // Skill
        var skillDir = Path.Combine(fx.MobaDevPath, "skills", "mobaflow-delivery-handoff");
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "SKILL.md"),
            "---\nname: mobaflow-delivery-handoff\ndescription: Structured handoff skill\n---\n");

        var provider = new GovernanceProvider();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var result = await provider.GetGovernanceAsync(paths, CancellationToken.None);

        Assert.Equal("ok", result.Source.Status);
        Assert.Equal(4, result.Profiles.Count);
        Assert.Equal("P1", result.Profiles[0].Profile);
        Assert.Equal(2, result.Roles.Count);
        Assert.Equal(3, result.RiskClasses.Count);

        Assert.Equal(1, result.SyncedCount);
        Assert.Equal(1, result.DriftCount);
        Assert.Equal(1, result.MissingCount);
        Assert.Single(result.Skills);

        var syncedRule = Assert.Single(result.Agents, a => a.FileName == "mobaflow-agent-network.md");
        Assert.Equal("synced", syncedRule.SyncStatus);

        var driftedAgent = Assert.Single(result.Agents, a => a.FileName == "head-of-ai-strategy.md");
        Assert.Equal("drift", driftedAgent.SyncStatus);
        Assert.Equal("P3", driftedAgent.ProfileHint);

        var missingClaude = Assert.Single(result.Agents, a => a.FileName == "p1-final-reviewer.md");
        Assert.Equal("missing_local", missingClaude.SyncStatus);
        Assert.Equal("P1", missingClaude.ProfileHint);
    }

    [Fact]
    public async Task GetRuleDiffAsync_ComputesNormalizedDiffAndRejectsPathTraversal()
    {
        using var fx = new TempDashboardFixture();
        var repoAgyDir = Path.Combine(fx.MobaDevPath, "agents", "antigravity", "agents");
        var localAgyDir = Path.Combine(fx.GeminiConfigPath, "agents");
        Directory.CreateDirectory(repoAgyDir);
        Directory.CreateDirectory(localAgyDir);

        await File.WriteAllTextAsync(
            Path.Combine(repoAgyDir, "head-of-ai-strategy.md"),
            $"# Head of AI Strategy\nWorkspace: `{fx.MobaFlowPath}`\nLine Old\nFooter\n");

        await File.WriteAllTextAsync(
            Path.Combine(localAgyDir, "head-of-ai-strategy.md"),
            $"# Head of AI Strategy\nWorkspace: `{fx.MobaFlowPath.Replace('\\', '/')}`\nLine New\nExtra Line\nFooter\n");

        var provider = new GovernanceProvider();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var diff = await provider.GetRuleDiffAsync(
            paths,
            "Antigravity",
            "agent",
            "head-of-ai-strategy.md",
            CancellationToken.None);

        Assert.NotNull(diff);
        Assert.Equal("drift", diff.SyncStatus);
        Assert.Equal(2, diff.AddedLines);
        Assert.Equal(1, diff.RemovedLines);
        Assert.False(diff.Truncated);
        Assert.Contains(diff.Lines, l => l.Kind == "removed" && l.Text == "Line Old");
        Assert.Contains(diff.Lines, l => l.Kind == "added" && l.Text == "Line New");
        Assert.Contains(diff.Lines, l => l.Kind == "added" && l.Text == "Extra Line");
        Assert.Contains(diff.Lines, l => l.Kind == "context" && l.Text.Contains("<repo-root>/MOBAflow"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRuleDiffAsync(paths, "Antigravity", "agent", "../secret.md", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRuleDiffAsync(paths, "Antigravity", "agent", "sub/file.md", CancellationToken.None));
    }
}
