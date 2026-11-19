using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads capability profiles, roles, risk classes, skills, and local-vs-MOBAdev drift status
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
            notice = "MOBAflow Governance-Dokumente nicht gefunden; zeige nur MOBAdev-Bestand.";
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
        var filePath = Path.Combine(mobaFlowRepoPath, "docs", "AI-DEVELOPMENT.md");
        if (!File.Exists(filePath) || !PathWhitelistGuard.IsAllowedFilePath(filePath))
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
                Models: CleanMarkdown(row[1]),
                Strengths: CleanMarkdown(row[2])));
        }

        return result;
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

    private static List<AgentSyncItem> ScanAgentSyncItems(ResolvedDashboardPaths paths)
    {
        var targets = new (string Tool, string Kind, string RepoDir, string LocalDir)[]
        {
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
            ),
            (
                "Claude Code",
                "agent",
                Path.Combine(paths.MobaDevRepoPath, "agents", "claude", "agents"),
                Path.Combine(paths.ClaudeHomePath, "agents")
            )
        };

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
                    (not null, null) => ("missing_local", "Lokal nicht installiert"),
                    (null, not null) => ("missing_repo", "Fehlt im MOBAdev-Backup"),
                    _ => ("missing_local", "Quelle fehlt")
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
                SourceRepo: "MOBAdev",
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
        return $"Inhalt weicht ab (+{localOnly}/-{repoOnly} Zeilen)";
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
        return value
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    [GeneratedRegex(@"\b(P[1-4])\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileRegex();
}
