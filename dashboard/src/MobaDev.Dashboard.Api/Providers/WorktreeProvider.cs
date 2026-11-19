using MobaDev.Dashboard.Api.Configuration;
using MobaDev.Dashboard.Api.Contracts;

namespace MobaDev.Dashboard.Api.Providers;

/// <summary>
/// Reads git worktrees for MOBAflow strictly read-only and flags stale/merged worktrees above active ones.
/// </summary>
public sealed class WorktreeProvider
{
    private readonly ICommandRunner _commandRunner;

    public WorktreeProvider(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<(IReadOnlyList<WorktreeInfo> Worktrees, SourceHealth Source)> GetWorktreesAsync(
        ResolvedDashboardPaths paths,
        IReadOnlyList<PullRequestInfo> openPullRequests,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(paths.MobaFlowRepoPath) || !Directory.Exists(paths.MobaFlowRepoPath))
        {
            return ([], new SourceHealth("unavailable", now, "MOBAflow repository path not found."));
        }

        var result = await _commandRunner.RunAsync(
            "git",
            ["-C", paths.MobaFlowRepoPath, "worktree", "list", "--porcelain"],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return ([], new SourceHealth("unavailable", now, "Unable to execute git worktree list."));
        }

        var openPrByBranch = openPullRequests
            .Where(p => !string.IsNullOrWhiteSpace(p.Branch))
            .GroupBy(p => p.Branch, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Number, StringComparer.OrdinalIgnoreCase);

        var parsed = ParsePorcelain(result.StdOut, paths.MobaFlowRepoPath, openPrByBranch);
        var sorted = parsed
            .OrderBy(w => w.Status switch
            {
                "stale" => 0,
                "active" => 1,
                _ => 2
            })
            .ThenBy(w => w.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (sorted, new SourceHealth("ok", now));
    }

    private static List<WorktreeInfo> ParsePorcelain(
        string stdOut,
        string mobaFlowRepoPath,
        IReadOnlyDictionary<string, int> openPrByBranch)
    {
        var list = new List<WorktreeInfo>();
        var normalizedRoot = NormalizeSlashes(mobaFlowRepoPath).TrimEnd('/');

        string? currentPath = null;
        string currentHead = string.Empty;
        string currentBranch = "(detached)";

        void FlushStanza()
        {
            if (currentPath is null)
            {
                return;
            }

            var normalizedPath = NormalizeSlashes(currentPath).TrimEnd('/');
            var isMain = string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(currentBranch, "main", StringComparison.OrdinalIgnoreCase);

            var folderName = ToSafeRelativeFolderName(normalizedPath, normalizedRoot, isMain);

            int? linkedPr = null;
            string status;
            if (isMain)
            {
                status = "main";
            }
            else if (openPrByBranch.TryGetValue(currentBranch, out var prNumber))
            {
                linkedPr = prNumber;
                status = "active";
            }
            else if (string.Equals(currentBranch, "claude/review-from-start", StringComparison.OrdinalIgnoreCase))
            {
                // Standing Claude Monitor checkout worktree.
                status = "active";
            }
            else
            {
                status = "stale";
            }

            list.Add(new WorktreeInfo(
                FolderName: folderName,
                Branch: currentBranch,
                HeadShort: currentHead.Length > 8 ? currentHead[..8] : currentHead,
                IsMain: isMain,
                Status: status,
                LinkedPrNumber: linkedPr));

            currentPath = null;
            currentHead = string.Empty;
            currentBranch = "(detached)";
        }

        var normalizedStdOut = stdOut.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var rawLine in normalizedStdOut.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                FlushStanza();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                currentPath = line["worktree ".Length..].Trim();
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                currentHead = line["HEAD ".Length..].Trim();
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                var fullRef = line["branch ".Length..].Trim();
                currentBranch = fullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? fullRef["refs/heads/".Length..]
                    : fullRef;
            }
        }

        FlushStanza();
        return list;
    }

    private static string ToSafeRelativeFolderName(string normalizedPath, string normalizedRoot, bool isMain)
    {
        if (isMain)
        {
            return "MOBAflow (main)";
        }

        if (!string.IsNullOrEmpty(normalizedRoot) &&
            normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath[(normalizedRoot.Length + 1)..];
        }

        return Path.GetFileName(normalizedPath);
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}
