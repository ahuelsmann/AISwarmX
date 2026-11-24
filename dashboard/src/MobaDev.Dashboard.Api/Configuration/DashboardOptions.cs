namespace MobaDev.Dashboard.Api.Configuration;

/// <summary>
/// Configuration options for the read-only MOBAdev Agent Network Dashboard.
/// </summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    public string BindUrl { get; set; } = "http://127.0.0.1:5179";
    public string MobaDevRepoPath { get; set; } = string.Empty;
    public string MobaFlowRepoPath { get; set; } = string.Empty;
    public string ClaudeHomePath { get; set; } = string.Empty;
    public string CodexHomePath { get; set; } = string.Empty;
    public string AntigravityHomePath { get; set; } = string.Empty;
    public string GeminiConfigPath { get; set; } = string.Empty;
    public string GitHubRepository { get; set; } = "ahuelsmann/MOBAflow";
    public int GitHubProjectNumber { get; set; } = 1;
    public string GitHubProjectOwner { get; set; } = "ahuelsmann";
    public int LocalCacheTtlSeconds { get; set; } = 8;
    public int GitHubCacheTtlSeconds { get; set; } = 45;
    public string StaticWebRootPath { get; set; } = string.Empty;
    public string SwarmBusDbPath { get; set; } = string.Empty;

    /// <summary>
    /// Resolves effective repository and local tool paths without hardcoding user-specific paths in code.
    /// </summary>
    public ResolvedDashboardPaths ResolvePaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var mobaDevPath = ResolveMobaDevRepoPath(MobaDevRepoPath);

        var mobaFlowPath = !string.IsNullOrWhiteSpace(MobaFlowRepoPath)
            ? Path.GetFullPath(MobaFlowRepoPath)
            : ResolveDefaultMobaFlowPath(mobaDevPath);

        var claudeHome = ResolveOrDefault(ClaudeHomePath, Path.Combine(userProfile, ".claude"));
        var codexHome = ResolveOrDefault(CodexHomePath, Path.Combine(userProfile, ".codex"));
        var antigravityHome = ResolveOrDefault(AntigravityHomePath, Path.Combine(userProfile, ".gemini", "antigravity"));
        var geminiConfig = ResolveOrDefault(GeminiConfigPath, Path.Combine(userProfile, ".gemini", "config"));
        var staticWebRoot = ResolveOrDefault(
            StaticWebRootPath,
            string.IsNullOrEmpty(mobaDevPath)
                ? string.Empty
                : Path.Combine(mobaDevPath, "dashboard", "src", "MobaDev.Dashboard.Web", "dist"));
        var swarmBusDbPath = ResolveOrDefault(
            SwarmBusDbPath,
            Path.Combine(localAppData, "MOBAdev", "swarm-bus.db"));

        return new ResolvedDashboardPaths(
            mobaDevPath,
            mobaFlowPath,
            claudeHome,
            codexHome,
            antigravityHome,
            geminiConfig,
            staticWebRoot,
            swarmBusDbPath);
    }

    private static string ResolveOrDefault(string configuredPath, string fallbackPath)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? fallbackPath
            : Path.GetFullPath(configuredPath);
    }

    private static string ResolveMobaDevRepoPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var found = WalkUpForMobaDevRoot(seed);
            if (!string.IsNullOrEmpty(found))
            {
                return found;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string WalkUpForMobaDevRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            var manifestPath = Path.Combine(current.FullName, "manifest.md");
            var agentsPath = Path.Combine(current.FullName, "agents");
            if (File.Exists(manifestPath) && Directory.Exists(agentsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    private static string ResolveDefaultMobaFlowPath(string mobaDevPath)
    {
        if (string.IsNullOrWhiteSpace(mobaDevPath))
        {
            return string.Empty;
        }

        var parent = Directory.GetParent(mobaDevPath);
        if (parent is null)
        {
            return string.Empty;
        }

        var sibling = Path.Combine(parent.FullName, "MOBAflow");
        return Directory.Exists(sibling) ? sibling : sibling;
    }
}

/// <summary>
/// Resolved effective filesystem paths for the dashboard providers.
/// </summary>
public sealed record ResolvedDashboardPaths(
    string MobaDevRepoPath,
    string MobaFlowRepoPath,
    string ClaudeHomePath,
    string CodexHomePath,
    string AntigravityHomePath,
    string GeminiConfigPath,
    string StaticWebRootPath,
    string SwarmBusDbPath = "");
