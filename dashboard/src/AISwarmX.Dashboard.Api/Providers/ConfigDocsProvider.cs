using System.Text;
using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;
using AISwarmX.Dashboard.Api.Security;

namespace AISwarmX.Dashboard.Api.Providers;

/// <summary>
/// Strictly allow-listed, read-only provider for AISwarmX configuration files and MOBAflow instructions.
/// Never accesses files outside predefined Markdown catalogs or files blocked by <see cref="PathWhitelistGuard"/>.
/// </summary>
public sealed class ConfigDocsProvider
{
    private const int MaxContentCharacters = 512 * 1024;

    public Task<ConfigDocsCatalogResponse> GetCatalogAsync(
        ResolvedDashboardPaths paths,
        string scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedScope = NormalizeScope(scope);
        var (repository, repoRoot, entries) = DiscoverAllowListedFiles(paths, normalizedScope);

        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
        {
            return Task.FromResult(new ConfigDocsCatalogResponse(
                GeneratedAtUtc: now,
                Scope: normalizedScope,
                Repository: repository,
                Files: [],
                Source: new SourceHealth("unavailable", now, $"Repository path for '{repository}' not found.")));
        }

        var items = entries.Select(e => e.Item).ToList();
        return Task.FromResult(new ConfigDocsCatalogResponse(
            GeneratedAtUtc: now,
            Scope: normalizedScope,
            Repository: repository,
            Files: items,
            Source: new SourceHealth("ok", now)));
    }

    public Task<ConfigDocContentResponse?> GetDocumentContentAsync(
        ResolvedDashboardPaths paths,
        string scope,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedScope = NormalizeScope(scope);

        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains("..", StringComparison.Ordinal) ||
            relativePath.Contains('\0'))
        {
            throw new ArgumentException("Invalid relative document path.", nameof(relativePath));
        }

        var normalizedRelative = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        var (_, _, entries) = DiscoverAllowListedFiles(paths, normalizedScope);

        var match = entries.FirstOrDefault(e =>
            string.Equals(e.Item.RelativePath, normalizedRelative, StringComparison.OrdinalIgnoreCase));

        if (match is null || !PathWhitelistGuard.IsAllowedFilePath(match.FullPath) || !File.Exists(match.FullPath))
        {
            return Task.FromResult<ConfigDocContentResponse?>(null);
        }

        var raw = File.ReadAllText(match.FullPath, Encoding.UTF8);
        var truncated = raw.Length > MaxContentCharacters;
        var content = truncated ? raw[..MaxContentCharacters] : raw;
        var normalizedLineEndings = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lineCount = string.IsNullOrEmpty(normalizedLineEndings)
            ? 0
            : normalizedLineEndings.Split('\n').Length;

        return Task.FromResult<ConfigDocContentResponse?>(new ConfigDocContentResponse(
            Scope: normalizedScope,
            RelativePath: match.Item.RelativePath,
            FileName: match.Item.FileName,
            Group: match.Item.Group,
            Repository: match.Item.Repository,
            SizeBytes: match.Item.SizeBytes,
            LineCount: lineCount,
            LastModifiedUtc: match.Item.LastModifiedUtc,
            Truncated: truncated,
            Content: normalizedLineEndings));
    }

    private static string NormalizeScope(string scope)
    {
        if (string.Equals(scope, "aiswarmx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope, "mobadev", StringComparison.OrdinalIgnoreCase))
        {
            return "aiswarmx";
        }

        throw new ArgumentException("Unsupported config docs scope. Expected 'aiswarmx' (or legacy 'mobadev').", nameof(scope));
    }

    private static (string Repository, string RepoRoot, List<CatalogEntry> Entries) DiscoverAllowListedFiles(
        ResolvedDashboardPaths paths,
        string scope)
    {
        var repo = "ahuelsmann/AISwarmX";
        var root = paths.MobaDevRepoPath;
        var entries = new List<CatalogEntry>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return (repo, root, entries);
        }

        AddSingleIfAllowed(entries, scope, repo, root, "manifest.md", "Core");
        AddSingleIfAllowed(entries, scope, repo, root, "README.md", "Core");
        AddSingleIfAllowed(entries, scope, repo, root, "CHANGELOG.md", "Core");
        AddDirectoryMarkdown(entries, scope, repo, root, Path.Combine("agents", "claude", "agents"), "Claude Agents");
        AddDirectoryMarkdown(entries, scope, repo, root, Path.Combine("agents", "antigravity", "agents"), "Antigravity Agents");
        AddDirectoryMarkdown(entries, scope, repo, root, Path.Combine("agents", "antigravity", "rules"), "Antigravity Rules");
        AddSkillsMarkdown(entries, scope, repo, root);
        AddDirectoryMarkdown(entries, scope, repo, root, "docs", "Docs");
        AddSingleIfAllowed(entries, scope, repo, root, "dashboard/DESIGN.md", "Dashboard");

        return (repo, root, entries);
    }

    private static void AddSingleIfAllowed(
        List<CatalogEntry> entries,
        string scope,
        string repo,
        string repoRoot,
        string relativePath,
        string group)
    {
        var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        TryAddResolvedFile(entries, scope, repo, relativePath, fullPath, group);
    }

    private static void AddDirectoryMarkdown(
        List<CatalogEntry> entries,
        string scope,
        string repo,
        string repoRoot,
        string relativeDir,
        string group)
    {
        var dir = Path.Combine(repoRoot, relativeDir);
        if (!Directory.Exists(dir))
        {
            return;
        }

        var normalizedDir = relativeDir.Replace('\\', '/').TrimEnd('/');
        foreach (var file in Directory
                     .EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.'))
            {
                continue;
            }

            TryAddResolvedFile(entries, scope, repo, $"{normalizedDir}/{fileName}", file, group);
        }
    }

    private static void AddSkillsMarkdown(
        List<CatalogEntry> entries,
        string scope,
        string repo,
        string repoRoot)
    {
        var skillsDir = Path.Combine(repoRoot, "skills");
        if (!Directory.Exists(skillsDir))
        {
            return;
        }

        foreach (var subDir in Directory
                     .EnumerateDirectories(skillsDir)
                     .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            var skillName = Path.GetFileName(subDir);
            if (skillName.StartsWith('.'))
            {
                continue;
            }

            var skillFile = Path.Combine(subDir, "SKILL.md");
            TryAddResolvedFile(entries, scope, repo, $"skills/{skillName}/SKILL.md", skillFile, "Skills");
        }
    }

    private static void TryAddResolvedFile(
        List<CatalogEntry> entries,
        string scope,
        string repo,
        string relativePath,
        string fullPath,
        string group)
    {
        if (!File.Exists(fullPath) || !PathWhitelistGuard.IsAllowedFilePath(fullPath))
        {
            return;
        }

        var info = new FileInfo(fullPath);
        entries.Add(new CatalogEntry(
            Item: new ConfigDocItem(
                Scope: scope,
                RelativePath: relativePath.Replace('\\', '/'),
                FileName: info.Name,
                Group: group,
                Repository: repo,
                SizeBytes: info.Length,
                LastModifiedUtc: new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)),
            FullPath: fullPath));
    }

    private sealed record CatalogEntry(ConfigDocItem Item, string FullPath);
}
