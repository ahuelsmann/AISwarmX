using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Contracts;

namespace AISwarmX.Dashboard.Api.Providers;

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
        var checkedWorktrees = await PopulateDirtyFlagsAsync(parsed, cancellationToken).ConfigureAwait(false);
        var sorted = checkedWorktrees
            .OrderBy(w => w.Status switch
            {
                "stale" when !w.IsDirty => 0,
                "stale" => 1,
                "active" => 2,
                _ => 3
            })
            .ThenBy(w => w.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (sorted, new SourceHealth("ok", now));
    }

    private async Task<List<WorktreeInfo>> PopulateDirtyFlagsAsync(
        IReadOnlyList<ParsedWorktree> parsed,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(6, 6);
        var tasks = parsed.Select(async entry =>
        {
            if (entry.Info.IsMain || !Directory.Exists(entry.FullPath))
            {
                return entry.Info;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var statusResult = await _commandRunner.RunAsync(
                    "git",
                    ["-C", entry.FullPath, "status", "--porcelain"],
                    workingDirectory: null,
                    timeout: TimeSpan.FromSeconds(4),
                    cancellationToken).ConfigureAwait(false);

                var isDirty = statusResult.Succeeded && !string.IsNullOrWhiteSpace(statusResult.StdOut);
                return entry.Info with { IsDirty = isDirty };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                return entry.Info;
            }
            finally
            {
                gate.Release();
            }
        });

        return [.. await Task.WhenAll(tasks).ConfigureAwait(false)];
    }

    public async Task<(IReadOnlyList<BranchHygieneInfo> Branches, int StaleLocalCount, int StaleRemoteCount)> GetBranchHygieneAsync(
        ResolvedDashboardPaths paths,
        IReadOnlyList<PullRequestInfo> openPullRequests,
        CancellationToken cancellationToken)
    {
        var openPrByBranch = openPullRequests
            .Where(p => !string.IsNullOrWhiteSpace(p.Branch))
            .GroupBy(p => p.Branch, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Number, StringComparer.OrdinalIgnoreCase);

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sameRepo = !string.IsNullOrWhiteSpace(paths.MobaFlowRepoPath)
            && !string.IsNullOrWhiteSpace(paths.MobaDevRepoPath)
            && string.Equals(
                Path.GetFullPath(paths.MobaFlowRepoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(paths.MobaDevRepoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                pathComparison);

        var primaryLabel = sameRepo ? "AISwarmX" : "MOBAflow";
        var flowLocalTask = ReadLocalBranchesAsync(paths.MobaFlowRepoPath, primaryLabel, openPrByBranch, cancellationToken);
        var flowRemoteTask = ReadRemoteOriginBranchesAsync(paths.MobaFlowRepoPath, primaryLabel, openPrByBranch, cancellationToken);
        var devLocalTask = sameRepo
            ? Task.FromResult(new List<BranchHygieneInfo>())
            : ReadLocalBranchesAsync(paths.MobaDevRepoPath, "AISwarmX", openPrByBranch: null, cancellationToken);

        await Task.WhenAll(flowLocalTask, flowRemoteTask, devLocalTask).ConfigureAwait(false);

        var all = new List<BranchHygieneInfo>();
        all.AddRange(flowLocalTask.Result);
        all.AddRange(flowRemoteTask.Result);
        all.AddRange(devLocalTask.Result);

        var sorted = all
            .OrderBy(b => b.Status == "stale" ? 0 : 1)
            .ThenBy(b => b.Scope == "local" ? 0 : 1)
            .ThenBy(b => b.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staleLocalCount = sorted.Count(b => b.Status == "stale" && b.Scope == "local");
        var staleRemoteCount = sorted.Count(b => b.Status == "stale" && b.Scope == "remote");
        return (sorted, staleLocalCount, staleRemoteCount);
    }

    private async Task<List<BranchHygieneInfo>> ReadLocalBranchesAsync(
        string repoPath,
        string repositoryLabel,
        IReadOnlyDictionary<string, int>? openPrByBranch,
        CancellationToken cancellationToken)
    {
        var list = new List<BranchHygieneInfo>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return list;
        }

        var result = await _commandRunner.RunAsync(
            "git",
            ["-C", repoPath, "for-each-ref", "--format=%(refname:short)|%(upstream:track)", "refs/heads"],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return list;
        }

        var normalized = result.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var rawLine in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Split('|', 2);
            var name = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "main", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var track = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var upstreamGone = track.Contains("[gone]", StringComparison.OrdinalIgnoreCase);
            var (status, linkedPr) = ClassifyBranchStatus(name, openPrByBranch);

            list.Add(new BranchHygieneInfo(
                Repository: repositoryLabel,
                Name: name,
                Scope: "local",
                Status: status,
                UpstreamGone: upstreamGone,
                LinkedPrNumber: linkedPr));
        }

        return list;
    }

    private async Task<List<BranchHygieneInfo>> ReadRemoteOriginBranchesAsync(
        string repoPath,
        string repositoryLabel,
        IReadOnlyDictionary<string, int> openPrByBranch,
        CancellationToken cancellationToken)
    {
        var list = new List<BranchHygieneInfo>();
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return list;
        }

        var result = await _commandRunner.RunAsync(
            "git",
            ["-C", repoPath, "for-each-ref", "--format=%(refname:short)", "refs/remotes/origin"],
            workingDirectory: null,
            timeout: TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return list;
        }

        var normalized = result.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var rawLine in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var rawName = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(rawName) ||
                string.Equals(rawName, "origin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawName, "origin/HEAD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawName, "origin/main", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var branchName = rawName.StartsWith("origin/", StringComparison.OrdinalIgnoreCase)
                ? rawName["origin/".Length..]
                : rawName;

            if (string.IsNullOrWhiteSpace(branchName) ||
                string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (status, linkedPr) = ClassifyBranchStatus(branchName, openPrByBranch);
            list.Add(new BranchHygieneInfo(
                Repository: repositoryLabel,
                Name: branchName,
                Scope: "remote",
                Status: status,
                UpstreamGone: false,
                LinkedPrNumber: linkedPr));
        }

        return list;
    }

    private static (string Status, int? LinkedPr) ClassifyBranchStatus(
        string branchName,
        IReadOnlyDictionary<string, int>? openPrByBranch)
    {
        if (openPrByBranch is null)
        {
            return ("stale", null);
        }

        if (openPrByBranch.TryGetValue(branchName, out var prNumber))
        {
            return ("active", prNumber);
        }

        if (string.Equals(branchName, "claude/review-from-start", StringComparison.OrdinalIgnoreCase))
        {
            return ("active", null);
        }

        return ("stale", null);
    }

    private static List<ParsedWorktree> ParsePorcelain(
        string stdOut,
        string mobaFlowRepoPath,
        IReadOnlyDictionary<string, int> openPrByBranch)
    {
        var list = new List<ParsedWorktree>();
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

            var fullPath = currentPath.Trim();
            var normalizedPath = NormalizeSlashes(fullPath).TrimEnd('/');
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

            var info = new WorktreeInfo(
                FolderName: folderName,
                Branch: currentBranch,
                HeadShort: currentHead.Length > 8 ? currentHead[..8] : currentHead,
                IsMain: isMain,
                Status: status,
                LinkedPrNumber: linkedPr);

            list.Add(new ParsedWorktree(info, fullPath));

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

    private sealed record ParsedWorktree(WorktreeInfo Info, string FullPath);
}
