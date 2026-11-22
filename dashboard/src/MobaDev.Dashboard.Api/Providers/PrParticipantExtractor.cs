using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Providers;

public sealed record PrCommentInput(string AuthorLogin, string Body);

/// <summary>
/// Extracts participating AI agents, capability profiles (P1-P4), models, and roles
/// for a MOBAflow pull request purely from concrete branch, PR body, and comment evidence.
/// </summary>
public static partial class PrParticipantExtractor
{
    public static IReadOnlyList<TaskParticipantInfo> Extract(
        string headRefName,
        string body,
        IReadOnlyList<PrCommentInput> comments)
    {
        var candidates = new List<(int Order, TaskParticipantInfo Info)>();

        AddWriterFromEvidence(headRefName, body, comments, candidates);

        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Body))
            {
                continue;
            }

            AddParticipantsFromComment(comment.AuthorLogin, comment.Body, candidates);
        }

        // Deduplicate by (Role, Tool, Profile), preferring higher-specificity outcomes and latest evidence.
        var grouped = candidates
            .GroupBy(c => (c.Info.Role, c.Info.Tool, c.Info.Profile))
            .Select(g => g
                .OrderBy(item => item.Order)
                .ThenByDescending(item => StatusPriority(item.Info.Status))
                .First())
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Info.Role, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Info)
            .ToList();

        return grouped;
    }

    public static TaskParticipantInfo? TryParseKanbanWriter(string? writerField)
    {
        if (string.IsNullOrWhiteSpace(writerField))
        {
            return null;
        }

        var trimmed = writerField.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("-", StringComparison.Ordinal) ||
            trimmed.Equals("—", StringComparison.Ordinal) ||
            trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("tbd", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return BuildWriterParticipant(
            trimmed,
            branch: string.Empty,
            status: "Active",
            evidenceSource: "Kanban Writer");
    }

    private static TaskParticipantInfo BuildWriterParticipant(
        string writerText,
        string branch,
        string status,
        string evidenceSource)
    {
        var profileMatch = ProfileCodeRegex().Match(writerText);

        string tool;
        string agent;
        if (writerText.Contains("Codex", StringComparison.OrdinalIgnoreCase) ||
            writerText.Contains("GPT", StringComparison.OrdinalIgnoreCase) ||
            writerText.Contains("Sol", StringComparison.OrdinalIgnoreCase) ||
            branch.StartsWith("codex/", StringComparison.OrdinalIgnoreCase))
        {
            tool = "ChatGPT Codex";
            agent = "Codex Writer";
        }
        else if (writerText.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) ||
                 writerText.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
                 writerText.Contains("Hydrogen", StringComparison.OrdinalIgnoreCase) ||
                 writerText.Contains("Argon", StringComparison.OrdinalIgnoreCase) ||
                 writerText.Contains("Neon", StringComparison.OrdinalIgnoreCase) ||
                 branch.StartsWith("antigravity/", StringComparison.OrdinalIgnoreCase))
        {
            tool = "Antigravity";
            agent = "Antigravity Writer";
        }
        else
        {
            tool = "Claude Code";
            agent = "Claude Writer";
        }

        var profile = profileMatch.Success
            ? profileMatch.Groups[1].Value.ToUpperInvariant()
            : tool switch
            {
                "ChatGPT Codex" => "P2",
                "Antigravity" => "P3",
                _ => "P1"
            };

        var model = (tool, profile) switch
        {
            ("Claude Code", "P4") => "Claude Sonnet 5.5",
            ("Claude Code", _) => "Claude Opus 5.5",
            ("ChatGPT Codex", _) => "GPT-6 Astra",
            ("Antigravity", "P4") => "Gemini 4 Hydrogen",
            _ => "Gemini 4 Argon"
        };

        return new TaskParticipantInfo(
            Agent: agent,
            Tool: tool,
            Role: "Writer",
            Profile: profile,
            Model: model,
            Status: status,
            EvidenceSource: evidenceSource);
    }

    private static void AddWriterFromEvidence(
        string branch,
        string body,
        IReadOnlyList<PrCommentInput> comments,
        List<(int Order, TaskParticipantInfo Info)> candidates)
    {
        var writerHeader = WriterHeaderLineRegex().Match(body);
        if (writerHeader.Success)
        {
            candidates.Add((0, BuildWriterParticipant(
                writerHeader.Groups[1].Value.Trim(),
                branch,
                status: "Implemented",
                evidenceSource: "Slice Header")));
            return;
        }

        if (branch.StartsWith("claude/", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Generated with [Claude Code]", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Generated by [Claude Code]", StringComparison.OrdinalIgnoreCase))
        {
            var isP4 = body.Contains("P4", StringComparison.Ordinal) &&
                       !body.Contains("P1", StringComparison.Ordinal);

            candidates.Add((0, new TaskParticipantInfo(
                Agent: "Claude Writer",
                Tool: "Claude Code",
                Role: "Writer",
                Profile: isP4 ? "P4" : "P1",
                Model: isP4 ? "Claude Sonnet 5.5" : "Claude Opus 5.5",
                Status: "Implemented",
                EvidenceSource: branch.StartsWith("claude/", StringComparison.OrdinalIgnoreCase)
                    ? "Branch & PR Body"
                    : "PR Body")));
            return;
        }

        if (branch.StartsWith("codex/", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Codex draft", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add((0, new TaskParticipantInfo(
                Agent: "Codex Writer",
                Tool: "ChatGPT Codex",
                Role: "Writer",
                Profile: "P2",
                Model: "GPT-6 Astra",
                Status: "Implemented",
                EvidenceSource: "Branch")));
            return;
        }

        if (branch.StartsWith("antigravity/", StringComparison.OrdinalIgnoreCase) ||
            comments.Any(c => c.Body.Contains("Head of AI Strategy", StringComparison.OrdinalIgnoreCase) &&
                              c.Body.Contains("setzt die Maintainer-Entscheidung um", StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add((0, new TaskParticipantInfo(
                Agent: "Antigravity Writer",
                Tool: "Antigravity",
                Role: "Writer",
                Profile: "P3",
                Model: "Gemini 4 Argon",
                Status: "Implemented",
                EvidenceSource: "Branch / PR Comment")));
        }
    }

    private static void AddParticipantsFromComment(
        string authorLogin,
        string text,
        List<(int Order, TaskParticipantInfo Info)> candidates)
    {
        // Codex GitHub PR connector
        if (string.Equals(authorLogin, "chatgpt-codex-connector", StringComparison.OrdinalIgnoreCase))
        {
            var status = text.Contains("usage limits", StringComparison.OrdinalIgnoreCase)
                ? "Quota Limit"
                : text.Contains("\"has_any_findings\":false", StringComparison.OrdinalIgnoreCase) ||
                  text.Contains("Didn't find any major issues", StringComparison.OrdinalIgnoreCase)
                    ? "PASS"
                    : text.Contains("\"has_any_findings\":true", StringComparison.OrdinalIgnoreCase)
                        ? "Findings"
                        : "Reviewed";

            candidates.Add((2, new TaskParticipantInfo(
                Agent: "Codex PR Reviewer",
                Tool: "ChatGPT Codex",
                Role: "Companion / Auto-Review",
                Profile: "P2",
                Model: "GPT-6 Astra (Codex)",
                Status: status,
                EvidenceSource: "chatgpt-codex-connector")));
        }

        // COO / Claude Monitor
        if (text.Contains("COO gate:", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("**Go (COO", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("COO Go", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("relayed by the COO", StringComparison.OrdinalIgnoreCase))
        {
            var status = text.Contains("Arming", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("**Go (COO", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("reviews clean", StringComparison.OrdinalIgnoreCase)
                ? "Approved"
                : "Active";

            candidates.Add((1, new TaskParticipantInfo(
                Agent: "Claude Monitor",
                Tool: "Claude Code",
                Role: "COO (Steering & Gate)",
                Profile: "P1",
                Model: "Claude Opus 5.5",
                Status: status,
                EvidenceSource: "COO Comment")));
        }

        // Companion Reviewer P2 / P1
        if (text.Contains("P2 companion", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Codex companion", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add((2, new TaskParticipantInfo(
                Agent: "Codex Companion",
                Tool: "ChatGPT Codex",
                Role: "Companion Reviewer",
                Profile: "P2",
                Model: "GPT-6 Astra",
                Status: "Reviewed",
                EvidenceSource: "Companion Review")));
        }

        if (text.Contains("P1 companion", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add((2, new TaskParticipantInfo(
                Agent: "Claude Companion",
                Tool: "Claude Code",
                Role: "Companion Reviewer",
                Profile: "P1",
                Model: "Claude Opus 5.5",
                Status: "Reviewed",
                EvidenceSource: "Companion Review")));
        }

        // Domain Specialists
        if (text.Contains("Head of UI/UX", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add((3, new TaskParticipantInfo(
                Agent: "Head of UI/UX",
                Tool: "Antigravity",
                Role: "UI/UX Specialist",
                Profile: "P3",
                Model: "Gemini 4 Argon",
                Status: "Consulted · PASS",
                EvidenceSource: "PR Comment")));
        }

        if (text.Contains("Head of AI Strategy", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add((3, new TaskParticipantInfo(
                Agent: "Antigravity Monitor",
                Tool: "Antigravity",
                Role: "Head of AI Strategy",
                Profile: "P3",
                Model: "Orchestrator (Neon)",
                Status: "Involved",
                EvidenceSource: "PR Comment")));
        }

        // Final P1 Reviewer
        if (text.Contains("Final review (P1", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("P1 final review", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("P1 re-review", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Independent Claude review", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Independent P1 review", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Re-reviews (fresh read-only Claude Opus", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Model gate:", StringComparison.OrdinalIgnoreCase))
        {
            var status = text.Contains("Model gate: BLOCKED", StringComparison.OrdinalIgnoreCase)
                ? "Blocked"
                : text.Contains("clean", StringComparison.OrdinalIgnoreCase) ||
                  text.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
                  text.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase) ||
                  text.Contains("no Blocker/High", StringComparison.OrdinalIgnoreCase)
                    ? "PASS"
                    : "Reviewed";

            candidates.Add((4, new TaskParticipantInfo(
                Agent: "Fresh Claude Reviewer",
                Tool: "Claude Code",
                Role: "Final Reviewer",
                Profile: "P1",
                Model: "Claude Opus 5.5",
                Status: status,
                EvidenceSource: "P1 Final Review")));
        }

        // Cross-Family Verifier P2 (Codex)
        if (text.Contains("Cross-family verification (P2", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Technical Director/Codex", StringComparison.OrdinalIgnoreCase))
        {
            var status = text.Contains("PASS", StringComparison.OrdinalIgnoreCase) ? "PASS" : "Reviewed";
            candidates.Add((5, new TaskParticipantInfo(
                Agent: "Codex Monitor",
                Tool: "ChatGPT Codex",
                Role: "Cross-Family Verifier (TD/CPM)",
                Profile: "P2",
                Model: "Sol 6.2",
                Status: status,
                EvidenceSource: "Cross-Family Check")));
        }

        // Cross-Family Verifier P3 (Antigravity)
        if (text.Contains("Cross-family verification (P3", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cross-family P3", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cross-family verification by Antigravity", StringComparison.OrdinalIgnoreCase))
        {
            var status = text.Contains("PASS", StringComparison.OrdinalIgnoreCase) ? "PASS" : "Reviewed";
            candidates.Add((5, new TaskParticipantInfo(
                Agent: "Antigravity Verifier",
                Tool: "Antigravity",
                Role: "Cross-Family Verifier",
                Profile: "P3",
                Model: "Gemini 4 Argon",
                Status: status,
                EvidenceSource: "Cross-Family Check")));
        }
    }

    private static int StatusPriority(string status) => status switch
    {
        "Blocked" => 6,
        "PASS" => 5,
        "Approved" => 4,
        "Findings" => 3,
        "Quota Limit" => 2,
        "Active" => 1,
        _ => 0
    };

    [GeneratedRegex(@"^[-*]\s*Writer\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex WriterHeaderLineRegex();

    [GeneratedRegex(@"\b(P[1-4])\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileCodeRegex();
}
