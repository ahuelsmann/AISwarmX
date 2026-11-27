namespace MobaDev.SwarmBus.Core.Locking;

/// <summary>
/// Normalizes repo-relative path patterns and detects hierarchical prefix or glob overlaps scoped by workspace.
/// </summary>
public static class PathPatternMatcher
{
    private readonly record struct DpOverlapSpec(
        int LengthA,
        int LengthB,
        Func<int, bool> IsWildA,
        Func<int, bool> IsWildB,
        Func<int, int, bool> PairMatches);

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
    public static string NormalizePath(string? rawPath) => NormalizeCore(rawPath, lowercase: true);

    /// <summary>
    /// Normalizes a path for storage while preserving original casing and trimming trailing slashes / <c>/*</c>.
    /// </summary>
    public static string CanonicalizeForStorage(string? rawPath) => NormalizeCore(rawPath, lowercase: false);

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

    private static string NormalizeCore(string? rawPath, bool lowercase)
    {
        var clean = (rawPath ?? string.Empty).Trim().Replace('\\', '/');
        if (lowercase)
        {
            clean = clean.ToLowerInvariant();
        }

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

    private static bool SegmentArraysOverlap(string[] segsA, string[] segsB) =>
        DpGridOverlap(new DpOverlapSpec(
            LengthA: segsA.Length,
            LengthB: segsB.Length,
            IsWildA: i => string.Equals(segsA[i], "**", StringComparison.Ordinal),
            IsWildB: j => string.Equals(segsB[j], "**", StringComparison.Ordinal),
            PairMatches: (i, j) => SingleSegmentGlobsOverlap(segsA[i], segsB[j])));

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

        return DpGridOverlap(new DpOverlapSpec(
            LengthA: segA.Length,
            LengthB: segB.Length,
            IsWildA: i => segA[i] == '*',
            IsWildB: j => segB[j] == '*',
            PairMatches: (i, j) => segA[i] == segB[j]));
    }

    private static bool DpGridOverlap(DpOverlapSpec spec)
    {
        var dp = new bool[spec.LengthA + 1, spec.LengthB + 1];
        dp[spec.LengthA, spec.LengthB] = true;

        for (var i = spec.LengthA; i >= 0; i--)
        {
            for (var j = spec.LengthB; j >= 0; j--)
            {
                if (i < spec.LengthA || j < spec.LengthB)
                {
                    dp[i, j] = EvaluateDpCell(dp, i, j, spec);
                }
            }
        }

        return dp[0, 0];
    }

    private static bool EvaluateDpCell(bool[,] dp, int i, int j, DpOverlapSpec spec)
    {
        var aWild = i < spec.LengthA && spec.IsWildA(i);
        var bWild = j < spec.LengthB && spec.IsWildB(j);

        if (aWild && bWild)
        {
            return dp[i + 1, j] || dp[i, j + 1];
        }

        if (aWild)
        {
            return dp[i + 1, j] || (j < spec.LengthB && dp[i, j + 1]);
        }

        if (bWild)
        {
            return dp[i, j + 1] || (i < spec.LengthA && dp[i + 1, j]);
        }

        return i < spec.LengthA && j < spec.LengthB && spec.PairMatches(i, j) && dp[i + 1, j + 1];
    }
}
