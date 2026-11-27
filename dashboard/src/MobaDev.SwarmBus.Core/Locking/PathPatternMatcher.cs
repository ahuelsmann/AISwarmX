namespace MobaDev.SwarmBus.Core.Locking;

/// <summary>
/// Normalizes repo-relative path patterns and detects hierarchical prefix or glob overlaps scoped by workspace.
/// </summary>
public static class PathPatternMatcher
{
    /// <summary>
    /// Normalizes a workspace identifier, defaulting null/whitespace to <c>"default"</c>.
    /// </summary>
    public static string NormalizeWorkspaceId(string? workspaceId)
    {
        return string.IsNullOrWhiteSpace(workspaceId)
            ? Contracts.SwarmBusConstants.DefaultWorkspaceId
            : workspaceId.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a path pattern: converts <c>\</c> to <c>/</c>, lowercases, trims leading <c>./</c> and <c>/</c>,
    /// trims trailing <c>/</c>, and strips trailing <c>/*</c>.
    /// </summary>
    public static string NormalizePath(string? rawPath)
    {
        var clean = (rawPath ?? string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();
        while (clean.Contains("//", StringComparison.Ordinal))
        {
            clean = clean.Replace("//", "/", StringComparison.Ordinal);
        }

        while (clean.StartsWith("./", StringComparison.Ordinal))
        {
            clean = clean[2..];
        }

        clean = clean.TrimStart('/').TrimEnd('/');
        if (clean.EndsWith("/*", StringComparison.Ordinal))
        {
            clean = clean[..^2].TrimEnd('/');
        }

        return clean;
    }

    /// <summary>
    /// Normalizes a path for storage while preserving original casing and trimming trailing slashes / <c>/*</c>.
    /// </summary>
    public static string CanonicalizeForStorage(string? rawPath)
    {
        var clean = (rawPath ?? string.Empty).Trim().Replace('\\', '/');
        while (clean.Contains("//", StringComparison.Ordinal))
        {
            clean = clean.Replace("//", "/", StringComparison.Ordinal);
        }

        while (clean.StartsWith("./", StringComparison.Ordinal))
        {
            clean = clean[2..];
        }

        clean = clean.TrimStart('/').TrimEnd('/');
        if (clean.EndsWith("/*", StringComparison.Ordinal))
        {
            clean = clean[..^2].TrimEnd('/');
        }

        return clean;
    }

    /// <summary>
    /// Returns <c>true</c> when two locks in their respective workspaces overlap.
    /// Locks in different workspaces never collide.
    /// </summary>
    public static bool LocksOverlap(string? workspaceA, string? pathA, string? workspaceB, string? pathB)
    {
        if (!string.Equals(NormalizeWorkspaceId(workspaceA), NormalizeWorkspaceId(workspaceB), StringComparison.Ordinal))
        {
            return false;
        }

        return PathsOverlap(pathA, pathB);
    }

    /// <summary>
    /// Returns <c>true</c> if two normalized path patterns overlap via equality, directory-prefix containment,
    /// or <c>*</c> / <c>**</c> glob intersection.
    /// </summary>
    public static bool PathsOverlap(string? aRaw, string? bRaw)
    {
        var a = NormalizePath(aRaw);
        var b = NormalizePath(bRaw);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        var aHasWildcard = a.Contains('*', StringComparison.Ordinal);
        var bHasWildcard = b.Contains('*', StringComparison.Ordinal);

        if (!aHasWildcard && !bHasWildcard)
        {
            return a.StartsWith(b + "/", StringComparison.Ordinal) ||
                   b.StartsWith(a + "/", StringComparison.Ordinal);
        }

        var segsA = ToPatternSegments(a, aHasWildcard);
        var segsB = ToPatternSegments(b, bHasWildcard);
        return SegmentArraysOverlap(segsA, segsB);
    }

    private static string[] ToPatternSegments(string normalizedPath, bool hasWildcard)
    {
        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (hasWildcard)
        {
            return parts;
        }

        // Plain path/directory locks cover the path itself and everything beneath it.
        var expanded = new string[parts.Length + 1];
        Array.Copy(parts, expanded, parts.Length);
        expanded[^1] = "**";
        return expanded;
    }

    private static bool SegmentArraysOverlap(string[] segsA, string[] segsB)
    {
        var n = segsA.Length;
        var m = segsB.Length;
        var dp = new bool[n + 1, m + 1];
        dp[n, m] = true;

        for (var i = n; i >= 0; i--)
        {
            for (var j = m; j >= 0; j--)
            {
                if (i == n && j == m)
                {
                    continue;
                }

                var aIsStarStar = i < n && string.Equals(segsA[i], "**", StringComparison.Ordinal);
                var bIsStarStar = j < m && string.Equals(segsB[j], "**", StringComparison.Ordinal);

                if (aIsStarStar && bIsStarStar)
                {
                    dp[i, j] = dp[i + 1, j] || dp[i, j + 1];
                }
                else if (aIsStarStar)
                {
                    dp[i, j] = dp[i + 1, j] || (j < m && dp[i, j + 1]);
                }
                else if (bIsStarStar)
                {
                    dp[i, j] = dp[i, j + 1] || (i < n && dp[i + 1, j]);
                }
                else if (i < n && j < m && SingleSegmentGlobsOverlap(segsA[i], segsB[j]))
                {
                    dp[i, j] = dp[i + 1, j + 1];
                }
            }
        }

        return dp[0, 0];
    }

    private static bool SingleSegmentGlobsOverlap(string segA, string segB)
    {
        if (string.Equals(segA, segB, StringComparison.Ordinal))
        {
            return true;
        }

        if (!segA.Contains('*', StringComparison.Ordinal) && !segB.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        var n = segA.Length;
        var m = segB.Length;
        var dp = new bool[n + 1, m + 1];
        dp[n, m] = true;

        for (var i = n; i >= 0; i--)
        {
            for (var j = m; j >= 0; j--)
            {
                if (i == n && j == m)
                {
                    continue;
                }

                var aStar = i < n && segA[i] == '*';
                var bStar = j < m && segB[j] == '*';

                if (aStar && bStar)
                {
                    dp[i, j] = dp[i + 1, j] || dp[i, j + 1];
                }
                else if (aStar)
                {
                    dp[i, j] = dp[i + 1, j] || (j < m && dp[i, j + 1]);
                }
                else if (bStar)
                {
                    dp[i, j] = dp[i, j + 1] || (i < n && dp[i + 1, j]);
                }
                else if (i < n && j < m && segA[i] == segB[j])
                {
                    dp[i, j] = dp[i + 1, j + 1];
                }
            }
        }

        return dp[0, 0];
    }
}
