using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads capability profiles, roles, risk classes, skills, and local-vs-AISwarmX drift status
/// strictly from repository and allowed user config Markdown files.
/// </summary>
public sealed partial class GovernanceProvider
{
    public Task<GovernanceResponse> GetGovernanceAsync(ResolvedDashboardPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        var profiles = ParseProfiles(paths.MobaFlowRepoPath);
        var (roles, riskClasses) = ParseMasterCoordination(paths.MobaFlowRepoPath);
        var agents = ScanAgentSyncItems(paths);
        var skills = ScanSkills(paths.MobaDevRepoPath);

        var syncedCount = agents.Count(a => a.SyncStatus == "synced");
        var driftCount = agents.Count(a => a.SyncStatus == "drift");
        var missingCount = agents.Count(a => a.SyncStatus is "missing_local" or "missing_repo");

        string? notice = null;
        var sourceStatus = "ok";
        if (profiles.Count == 0 && roles.Count == 0)
        {
            sourceStatus = "degraded";
            notice = "MOBAflow governance documents not found; showing AISwarmX inventory only.";
        }

        return Task.FromResult(new GovernanceResponse(
            GeneratedAtUtc: now,
            Profiles: profiles,
            Roles: roles,
            RiskClasses: riskClasses,
            Agents: agents,
            Skills: skills,
            SyncedCount: syncedCount,
            DriftCount: driftCount,
            MissingCount: missingCount,
            Source: new SourceHealth(sourceStatus, now, notice)));
    }

    private static List<CapabilityProfileInfo> ParseProfiles(string mobaFlowRepoPath)
    {
        var filePath = ResolveAiDevelopmentDocPath(mobaFlowRepoPath);
        if (filePath is null)
        {
            return [];
        }

        var lines = File.ReadAllLines(filePath);
        var rows = ExtractMarkdownTable(lines, "## Capability profiles and models", "| Profile");
        var result = new List<CapabilityProfileInfo>();

        foreach (var row in rows)
        {
            if (row.Count < 3)
            {
                continue;
            }

            var rawProfile = CleanMarkdown(row[0]);
            var profileCode = rawProfile.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? rawProfile;
            var profileName = rawProfile.Length > profileCode.Length
                ? rawProfile[profileCode.Length..].Trim()
                : profileCode;

            result.Add(new CapabilityProfileInfo(
                Profile: profileCode,
                Name: profileName,
                Models: SanitizeDeprecatedModels(CleanMarkdown(row[1])),
                Strengths: CleanMarkdown(row[2])));
        }

        return result;
    }

    private static string? ResolveAiDevelopmentDocPath(string mobaFlowRepoPath)
    {
        if (string.IsNullOrWhiteSpace(mobaFlowRepoPath))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(mobaFlowRepoPath, "docs", "developer", "AI-DEVELOPMENT.md"),
            Path.Combine(mobaFlowRepoPath, "docs", "AI-DEVELOPMENT.md")
        };

        return candidates.FirstOrDefault(path =>
            File.Exists(path) && PathWhitelistGuard.IsAllowedFilePath(path));
    }

    private static string SanitizeDeprecatedModels(string models)
    {
        return models
            .Replace("Claude Fable 5.2 / Sonnet-class", "Claude Sonnet 5.5", StringComparison.OrdinalIgnoreCase)
            .Replace("Claude Fable 5.2", "Claude Sonnet 5.5", StringComparison.OrdinalIgnoreCase);
    }

    private static (List<RoleDefinitionInfo> Roles, List<RiskClassInfo> RiskClasses) ParseMasterCoordination(
        string mobaFlowRepoPath)
    {
        var filePath = Path.Combine(mobaFlowRepoPath, ".github", "instructions", "master-coordination.instructions.md");
        if (!File.Exists(filePath) || !PathWhitelistGuard.IsAllowedFilePath(filePath))
        {
            return ([], []);
        }

        var lines = File.ReadAllLines(filePath);
        var roleRows = ExtractMarkdownTable(lines, "## Capability profiles and roles", "| Role");
        var roles = roleRows
            .Where(r => r.Count >= 3)
            .Select(r => new RoleDefinitionInfo(
                CleanMarkdown(r[0]),
                CleanMarkdown(r[1]),
                CleanMarkdown(r[2])))
            .ToList();

        var riskRows = ExtractMarkdownTable(lines, "## Risk classes and domain matrix", "| Class");
        var riskClasses = new List<RiskClassInfo>();
        foreach (var row in riskRows)
        {
            if (row.Count < 3)
            {
                continue;
            }

            var rawClass = CleanMarkdown(row[0]);
            var code = rawClass.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? rawClass;
            var name = rawClass.Length > code.Length ? rawClass[code.Length..].Trim() : code;

            riskClasses.Add(new RiskClassInfo(
                Code: code,
                Name: name,
                Areas: CleanMarkdown(row[1]),
                RequiredDiversity: CleanMarkdown(row[2])));
        }

        return (roles, riskClasses);
    }

    private static IReadOnlyList<(string Tool, string Kind, string RepoDir, string LocalDir)> GetSyncTargets(
        ResolvedDashboardPaths paths) =>
    [
        (
            "Claude Code",
            "agent",
            Path.Combine(paths.MobaDevRepoPath, "agents", "claude", "agents"),
            Path.Combine(paths.ClaudeHomePath, "agents")
        ),
        (
            "Antigravity",
            "agent",
            Path.Combine(paths.MobaDevRepoPath, "agents", "antigravity", "agents"),
            Path.Combine(paths.GeminiConfigPath, "agents")
        ),
        (
            "Antigravity",
            "rule",
            Path.Combine(paths.MobaDevRepoPath, "agents", "antigravity", "rules"),
            Path.Combine(paths.GeminiConfigPath, "rules")
        )
    ];

    public Task<RuleDiffResponse?> GetRuleDiffAsync(
        ResolvedDashboardPaths paths,
        string tool,
        string kind,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.Contains('\0') ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid file name.", nameof(fileName));
        }

        var target = GetSyncTargets(paths).FirstOrDefault(t =>
            string.Equals(t.Tool, tool, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(target.Tool))
        {
            return Task.FromResult<RuleDiffResponse?>(null);
        }

        var repoPath = Path.Combine(target.RepoDir, fileName);
        var localPath = Path.Combine(target.LocalDir, fileName);

        if (!PathWhitelistGuard.IsAllowedFilePath(repoPath) ||
            !PathWhitelistGuard.IsAllowedFilePath(localPath))
        {
            throw new ArgumentException("File path is not permitted by whitelist.", nameof(fileName));
        }

        var repoExists = File.Exists(repoPath);
        var localExists = File.Exists(localPath);
        if (!repoExists && !localExists)
        {
            return Task.FromResult<RuleDiffResponse?>(null);
        }

        var repoRaw = repoExists ? File.ReadAllText(repoPath, Encoding.UTF8) : null;
        var localRaw = localExists ? File.ReadAllText(localPath, Encoding.UTF8) : null;

        var targetHash = repoRaw is not null ? ComputeNormalizedHash(repoRaw, paths.MobaFlowRepoPath) : null;
        var actualHash = localRaw is not null ? ComputeNormalizedHash(localRaw, paths.MobaFlowRepoPath) : null;

        var syncStatus = (targetHash, actualHash) switch
        {
            (not null, not null) when targetHash == actualHash => "synced",
            (not null, not null) => "drift",
            (not null, null) => "missing_local",
            (null, not null) => "missing_repo",
            _ => "missing_local"
        };

        var targetLines = repoRaw is not null
            ? NormalizeContentForComparison(repoRaw, paths.MobaFlowRepoPath).Split('\n')
            : [];
        var actualLines = localRaw is not null
            ? NormalizeContentForComparison(localRaw, paths.MobaFlowRepoPath).Split('\n')
            : [];

        const int contextLines = 2;
        const int maxLines = 200;

        var (diffLines, addedCount, removedCount, truncated) = BuildUnifiedDiffLines(
            targetLines,
            actualLines,
            contextLines,
            maxLines);

        return Task.FromResult<RuleDiffResponse?>(new RuleDiffResponse(
            Tool: target.Tool,
            Kind: target.Kind,
            FileName: fileName,
            SyncStatus: syncStatus,
            TargetHash: targetHash,
            ActualHash: actualHash,
            AddedLines: addedCount,
            RemovedLines: removedCount,
            Truncated: truncated,
            Lines: diffLines));
    }

    private static List<AgentSyncItem> ScanAgentSyncItems(ResolvedDashboardPaths paths)
    {
        var targets = GetSyncTargets(paths);

        var items = new List<AgentSyncItem>();
        foreach (var (tool, kind, repoDir, localDir) in targets)
        {
            var repoFiles = ListMarkdownFiles(repoDir);
            var localFiles = ListMarkdownFiles(localDir);

            var allFileNames = repoFiles.Keys
                .Union(localFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var fileName in allFileNames)
            {
                repoFiles.TryGetValue(fileName, out var repoPath);
                localFiles.TryGetValue(fileName, out var localPath);

                var repoContent = repoPath is not null ? File.ReadAllText(repoPath, Encoding.UTF8) : null;
                var localContent = localPath is not null ? File.ReadAllText(localPath, Encoding.UTF8) : null;

                var repoHash = repoContent is not null ? ComputeNormalizedHash(repoContent, paths.MobaFlowRepoPath) : null;
                var localHash = localContent is not null ? ComputeNormalizedHash(localContent, paths.MobaFlowRepoPath) : null;

                var (syncStatus, driftSummary) = (repoHash, localHash) switch
                {
                    (not null, not null) when repoHash == localHash => ("synced", (string?)null),
                    (not null, not null) => ("drift", DescribeDrift(repoContent!, localContent!, paths.MobaFlowRepoPath)),
                    (not null, null) => ("missing_local", "Not installed locally"),
                    (null, not null) => ("missing_repo", "Missing in AISwarmX backup"),
                    _ => ("missing_local", "Source missing")
                };

                var primaryText = repoContent ?? localContent ?? string.Empty;
                var (frontmatterName, description) = ParseFrontmatter(primaryText);
                var displayName = !string.IsNullOrWhiteSpace(frontmatterName)
                    ? frontmatterName
                    : Path.GetFileNameWithoutExtension(fileName);

                var profileHint = InferProfileHint(fileName, primaryText);

                items.Add(new AgentSyncItem(
                    Tool: tool,
                    Kind: kind,
                    Name: displayName,
                    FileName: fileName,
                    Description: description,
                    ProfileHint: profileHint,
                    SyncStatus: syncStatus,
                    RepoHash: repoHash,
                    LocalHash: localHash,
                    DriftSummary: driftSummary));
            }
        }

        return items;
    }

    private static List<SkillInfo> ScanSkills(string mobaDevRepoPath)
    {
        var skillsDir = Path.Combine(mobaDevRepoPath, "skills");
        if (!Directory.Exists(skillsDir))
        {
            return [];
        }

        var skills = new List<SkillInfo>();
        foreach (var dir in Directory.EnumerateDirectories(skillsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile) || !PathWhitelistGuard.IsAllowedFilePath(skillFile))
            {
                continue;
            }

            var content = File.ReadAllText(skillFile, Encoding.UTF8);
            var (name, description) = ParseFrontmatter(content);
            var dirName = Path.GetFileName(dir);

            skills.Add(new SkillInfo(
                Name: !string.IsNullOrWhiteSpace(name) ? name : dirName,
                Description: description ?? string.Empty,
                SourceRepo: "AISwarmX",
                RelativePath: $"skills/{dirName}/SKILL.md"));
        }

        return skills;
    }

    private static Dictionary<string, string> ListMarkdownFiles(string directory)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return map;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.') || !PathWhitelistGuard.IsAllowedFilePath(file))
            {
                continue;
            }

            map[fileName] = file;
        }

        return map;
    }

    internal static string ComputeNormalizedHash(string content, string mobaFlowRepoPath)
    {
        var normalized = NormalizeContentForComparison(content, mobaFlowRepoPath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes)[..12];
    }

    private static string NormalizeContentForComparison(string content, string mobaFlowRepoPath)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (!string.IsNullOrWhiteSpace(mobaFlowRepoPath))
        {
            var cleanPath = mobaFlowRepoPath.TrimEnd('\\', '/');
            var backslashVariant = cleanPath.Replace('/', '\\');
            var slashVariant = cleanPath.Replace('\\', '/');

            normalized = normalized
                .Replace(backslashVariant, "<repo-root>/MOBAflow", StringComparison.OrdinalIgnoreCase)
                .Replace(slashVariant, "<repo-root>/MOBAflow", StringComparison.OrdinalIgnoreCase);
        }

        return normalized;
    }

    private static string DescribeDrift(string repoContent, string localContent, string mobaFlowRepoPath)
    {
        var repoLines = NormalizeContentForComparison(repoContent, mobaFlowRepoPath).Split('\n');
        var localLines = NormalizeContentForComparison(localContent, mobaFlowRepoPath).Split('\n');

        var repoOnly = repoLines.Except(localLines).Count();
        var localOnly = localLines.Except(repoLines).Count();
        return $"Content differs (+{localOnly}/-{repoOnly} lines)";
    }

    private sealed record RawDiffOp(string Kind, int? TargetLineNumber, int? ActualLineNumber, string Text);

    private static (IReadOnlyList<DiffLineInfo> Lines, int AddedLines, int RemovedLines, bool Truncated)
        BuildUnifiedDiffLines(
            string[] targetLines,
            string[] actualLines,
            int contextLines,
            int maxLines)
    {
        if (targetLines.Length == 0 && actualLines.Length == 0)
        {
            return ([], 0, 0, false);
        }

        if (targetLines.Length == 0)
        {
            var truncatedOnlyAdded = actualLines.Length > maxLines;
            var addedOnly = actualLines
                .Take(maxLines)
                .Select((line, idx) => new DiffLineInfo("added", null, idx + 1, line))
                .ToList();
            return (addedOnly, actualLines.Length, 0, truncatedOnlyAdded);
        }

        if (actualLines.Length == 0)
        {
            var truncatedOnlyRemoved = targetLines.Length > maxLines;
            var removedOnly = targetLines
                .Take(maxLines)
                .Select((line, idx) => new DiffLineInfo("removed", idx + 1, null, line))
                .ToList();
            return (removedOnly, 0, targetLines.Length, truncatedOnlyRemoved);
        }

        var ops = ComputeDiffOps(targetLines, actualLines);
        var addedCount = ops.Count(o => o.Kind == "added");
        var removedCount = ops.Count(o => o.Kind == "removed");

        var include = new bool[ops.Count];
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == "context")
            {
                continue;
            }

            var start = Math.Max(0, i - contextLines);
            var end = Math.Min(ops.Count - 1, i + contextLines);
            for (var j = start; j <= end; j++)
            {
                include[j] = true;
            }
        }

        var result = new List<DiffLineInfo>();
        var truncated = false;
        var inHunk = false;

        for (var i = 0; i < ops.Count; i++)
        {
            if (!include[i])
            {
                inHunk = false;
                continue;
            }

            if (!inHunk)
            {
                var nextTarget = ops[i].TargetLineNumber ?? (FindLastKnownTargetLine(ops, i) + 1);
                var nextActual = ops[i].ActualLineNumber ?? (FindLastKnownActualLine(ops, i) + 1);
                if (result.Count >= maxLines)
                {
                    truncated = true;
                    break;
                }

                result.Add(new DiffLineInfo("hunk", null, null, $"@@ Target L{nextTarget} / Actual L{nextActual} @@"));
                inHunk = true;
            }

            if (result.Count >= maxLines)
            {
                truncated = true;
                break;
            }

            var op = ops[i];
            result.Add(new DiffLineInfo(op.Kind, op.TargetLineNumber, op.ActualLineNumber, op.Text));
        }

        return (result, addedCount, removedCount, truncated);
    }

    private static List<RawDiffOp> ComputeDiffOps(string[] targetLines, string[] actualLines)
    {
        var n = targetLines.Length;
        var m = actualLines.Length;

        if ((long)n * m > 400_000)
        {
            return ComputeLargeFileFallbackOps(targetLines, actualLines);
        }

        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = string.Equals(targetLines[i], actualLines[j], StringComparison.Ordinal)
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var ops = new List<RawDiffOp>(n + m);
        var ti = 0;
        var ai = 0;

        while (ti < n && ai < m)
        {
            if (string.Equals(targetLines[ti], actualLines[ai], StringComparison.Ordinal))
            {
                ops.Add(new RawDiffOp("context", ti + 1, ai + 1, targetLines[ti]));
                ti++;
                ai++;
            }
            else if (dp[ti + 1, ai] >= dp[ti, ai + 1])
            {
                ops.Add(new RawDiffOp("removed", ti + 1, null, targetLines[ti]));
                ti++;
            }
            else
            {
                ops.Add(new RawDiffOp("added", null, ai + 1, actualLines[ai]));
                ai++;
            }
        }

        while (ti < n)
        {
            ops.Add(new RawDiffOp("removed", ti + 1, null, targetLines[ti]));
            ti++;
        }

        while (ai < m)
        {
            ops.Add(new RawDiffOp("added", null, ai + 1, actualLines[ai]));
            ai++;
        }

        return ops;
    }

    private static List<RawDiffOp> ComputeLargeFileFallbackOps(string[] targetLines, string[] actualLines)
    {
        var ops = new List<RawDiffOp>();
        var prefix = 0;
        var minLen = Math.Min(targetLines.Length, actualLines.Length);
        while (prefix < minLen && string.Equals(targetLines[prefix], actualLines[prefix], StringComparison.Ordinal))
        {
            ops.Add(new RawDiffOp("context", prefix + 1, prefix + 1, targetLines[prefix]));
            prefix++;
        }

        var suffix = 0;
        while (suffix < (targetLines.Length - prefix) &&
               suffix < (actualLines.Length - prefix) &&
               string.Equals(
                   targetLines[targetLines.Length - 1 - suffix],
                   actualLines[actualLines.Length - 1 - suffix],
                   StringComparison.Ordinal))
        {
            suffix++;
        }

        for (var i = prefix; i < targetLines.Length - suffix; i++)
        {
            ops.Add(new RawDiffOp("removed", i + 1, null, targetLines[i]));
        }

        for (var j = prefix; j < actualLines.Length - suffix; j++)
        {
            ops.Add(new RawDiffOp("added", null, j + 1, actualLines[j]));
        }

        for (var s = suffix; s > 0; s--)
        {
            var ti = targetLines.Length - s;
            var ai = actualLines.Length - s;
            ops.Add(new RawDiffOp("context", ti + 1, ai + 1, targetLines[ti]));
        }

        return ops;
    }

    private static int FindLastKnownTargetLine(List<RawDiffOp> ops, int index)
    {
        for (var k = index - 1; k >= 0; k--)
        {
            if (ops[k].TargetLineNumber is int ln)
            {
                return ln;
            }
        }

        return 0;
    }

    private static int FindLastKnownActualLine(List<RawDiffOp> ops, int index)
    {
        for (var k = index - 1; k >= 0; k--)
        {
            if (ops[k].ActualLineNumber is int ln)
            {
                return ln;
            }
        }

        return 0;
    }

    private static (string? Name, string? Description) ParseFrontmatter(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (null, null);
        }

        var endIdx = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            return (null, null);
        }

        var yamlBlock = normalized[4..endIdx];
        string? name = null;
        string? description = null;

        foreach (var line in yamlBlock.Split('\n'))
        {
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                name = line[5..].Trim().Trim('"', '\'');
            }
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                description = line[12..].Trim().Trim('"', '\'');
            }
        }

        return (name, description);
    }

    private static string? InferProfileHint(string fileName, string content)
    {
        if (fileName.StartsWith("p1-", StringComparison.OrdinalIgnoreCase))
        {
            return "P1";
        }

        if (fileName.StartsWith("p2-", StringComparison.OrdinalIgnoreCase))
        {
            return "P2";
        }

        if (fileName.StartsWith("p4-", StringComparison.OrdinalIgnoreCase))
        {
            return "P4";
        }

        if (fileName.StartsWith("head-of-", StringComparison.OrdinalIgnoreCase))
        {
            return "P3";
        }

        var match = ProfileRegex().Match(content);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static List<List<string>> ExtractMarkdownTable(
        string[] lines,
        string sectionHeading,
        string headerPrefix)
    {
        var inSection = false;
        var inTable = false;
        var rows = new List<List<string>>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!inSection)
            {
                if (line.Equals(sectionHeading, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                }

                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (!inTable)
            {
                if (line.StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    inTable = true;
                }

                continue;
            }

            if (!line.StartsWith('|'))
            {
                if (rows.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (line.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line
                .Trim('|')
                .Split('|')
                .Select(c => c.Trim())
                .ToList();

            rows.Add(cells);
        }

        return rows;
    }

    private static string CleanMarkdown(string value)
    {
        var withoutLinks = MarkdownLinkRegex().Replace(value, "$1");
        return withoutLinks
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\b(P[1-4])\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileRegex();
}
