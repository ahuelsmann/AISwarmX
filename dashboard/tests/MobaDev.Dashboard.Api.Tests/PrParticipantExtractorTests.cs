using MobaDev.Dashboard.Api.Providers;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed class PrParticipantExtractorTests
{
    [Fact]
    public void Extract_IdentifiesWriterCooCompanionSpecialistsFinalReviewerAndCrossFamilyVerifiers()
    {
        var branch = "claude/rf09-contracts-lane";
        var body = """
            ## Summary
            RF-09 slice 2 of 3. Risk class M.
            🤖 Generated with [Claude Code](https://claude.com/claude-code)
            """;

        var comments = new List<PrCommentInput>
        {
            new("ahuelsmann", "**Go (COO, 2026-11-18 20:20 UTC)** for the corrected design note after P2 companion review."),
            new("chatgpt-codex-connector", "<!-- codex-review-state:v1 {\"code_review\":{\"has_any_findings\":false}} -->\nCodex Review: Didn't find any major issues."),
            new("ahuelsmann", "Head of UI/UX (Gemini 4 Argon) reviewed navigation clarity and layout."),
            new("ahuelsmann", "Head of AI Strategy aligned orchestration model routing."),
            new("ahuelsmann", "Final review (P1, fresh session): clean with no remaining findings."),
            new("ahuelsmann", "Cross-family verification (P2, Technical Director/Codex) on bb9b047c: PASS, no findings."),
            new("ahuelsmann", "Cross-family verification (P3, Gemini Argon): PASS, zero findings."),
            new("ahuelsmann", "COO gate: reviews clean, arming auto-merge.")
        };

        var participants = PrParticipantExtractor.Extract(branch, body, comments);

        Assert.Contains(participants, p => p.Role == "Writer" && p.Profile == "P1" && p.Model == "Claude Opus 5.5");
        Assert.Contains(participants, p => p.Role == "COO (Steering & Gate)" && p.Profile == "P1" && p.Status == "Approved");
        Assert.Contains(participants, p => p.Role == "Companion / Auto-Review" && p.Profile == "P2" && p.Status == "PASS");
        Assert.Contains(participants, p => p.Role == "Companion Reviewer" && p.Profile == "P2");
        Assert.Contains(participants, p => p.Role == "UI/UX Specialist" && p.Profile == "P3" && p.Model == "Gemini 4 Argon");
        Assert.Contains(participants, p => p.Role == "Head of AI Strategy" && p.Profile == "P3" && p.Model == "Orchestrator (Neon)");
        Assert.Contains(participants, p => p.Role == "Final Reviewer" && p.Profile == "P1" && p.Model == "Claude Opus 5.5" && p.Status == "PASS");
        Assert.Contains(participants, p => p.Role == "Cross-Family Verifier (TD/CPM)" && p.Profile == "P2" && p.Model == "Sol 6.2" && p.Status == "PASS");
        Assert.Contains(participants, p => p.Role == "Cross-Family Verifier" && p.Profile == "P3" && p.Model == "Gemini 4 Argon" && p.Status == "PASS");
    }

    [Fact]
    public void Extract_DetectsCodexRateLimitAndBlockedModelGate()
    {
        var participants = PrParticipantExtractor.Extract(
            "codex/strategy-mobaflow-kanban-board",
            "## Intent (Class L)",
            [
                new("chatgpt-codex-connector", "You have reached your Codex usage limits for code reviews."),
                new("ahuelsmann", "## Model gate: BLOCKED")
            ]);

        Assert.Contains(participants, p => p.Role == "Writer" && p.Profile == "P2" && p.Model == "GPT-6 Astra");
        Assert.Contains(participants, p => p.Role == "Companion / Auto-Review" && p.Status == "Quota Limit");
        Assert.Contains(participants, p => p.Role == "Final Reviewer" && p.Status == "Blocked");
    }

    [Fact]
    public void Extract_UsesExplicitSliceHeaderWriterProfileEvenWhenTemplateMentionsP1ToP4()
    {
        var body = """
            ## Slice header

            - Issue: #451
            - Risk class (H/M/L): L
            - Locked areas: `.editorconfig`, `AGENTS.md`, `plans/451-formatting-baseline.md`
            - Writer: P4 slice builder (Claude)
            - Companion / review profiles (P1-P4, or none for class L): none (class L)

            🤖 Generated with [Claude Code](https://claude.com/claude-code)
            """;

        var participants = PrParticipantExtractor.Extract("claude/rf10-rules", body, []);

        var writer = Assert.Single(participants, p => p.Role == "Writer");
        Assert.Equal("P4", writer.Profile);
        Assert.Equal("Claude Sonnet 5.5", writer.Model);
        Assert.Equal("Claude Code", writer.Tool);
        Assert.Equal("Slice Header", writer.EvidenceSource);
    }
}
