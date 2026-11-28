using Microsoft.Extensions.Configuration;

namespace AISwarmX.Dashboard.Api.Configuration;

/// <summary>
/// Configuration options for the read-only MOBAdev Agent Network Dashboard.
/// </summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";
    public const int DefaultBusyIdleAfterMinutes = 10;

    public string BindUrl { get; set; } = "http://127.0.0.1:5179";
    public string MobaDevRepoPath { get; set; } = string.Empty;
    public string MobaFlowRepoPath { get; set; } = string.Empty;
    public string ClaudeHomePath { get; set; } = string.Empty;
    public string CodexHomePath { get; set; } = string.Empty;
    public string AntigravityHomePath { get; set; } = string.Empty;
    public string GeminiConfigPath { get; set; } = string.Empty;
    public string GitHubRepository { get; set; } = "ahuelsmann/MOBAflow";
    public string[] MonitoredRepositories { get; set; } = [];
    public int GitHubProjectNumber { get; set; } = 1;
    public string GitHubProjectOwner { get; set; } = "ahuelsmann";
    public int LocalCacheTtlSeconds { get; set; } = 8;
    public int GitHubCacheTtlSeconds { get; set; } = 45;
    public int BusyIdleAfterMinutes { get; set; } = DefaultBusyIdleAfterMinutes;
    public string StaticWebRootPath { get; set; } = string.Empty;
    public string SwarmBusDbPath { get; set; } = string.Empty;
    public string SwarmConfigPath { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>
    /// Resolves effective <see cref="DashboardOptions"/> by optionally loading a <see cref="SwarmConfig"/>
    /// from <see cref="SwarmConfigPath"/> (or <c>MOBADEV_SWARM_CONFIG</c>), a <c>swarm.config.json</c>
    /// in the MOBAdev root, or <c>presets/mobaflow.swarm.json</c> when core workspace settings remain at defaults,
    /// while preserving any explicit overrides configured by callers or appsettings.
    /// </summary>
    public DashboardOptions ResolveWithSwarmConfig(
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        IReadOnlySet<string>? explicitOverrideKeys = null)
    {
        var effectiveConfigPath = ResolveOptionOrEnvironment(SwarmConfigPath, "MOBADEV_SWARM_CONFIG", environmentVariables);
        var effectiveWorkspaceId = ResolveOptionOrEnvironment(WorkspaceId, "MOBADEV_SWARM_WORKSPACE", environmentVariables);

        var mobaDevRoot = ResolveMobaDevRepoPath(MobaDevRepoPath);
        var resolvedConfigPath = TryResolveSwarmConfigFilePath(effectiveConfigPath, mobaDevRoot, out var isFallbackPreset);

        if (string.IsNullOrWhiteSpace(resolvedConfigPath))
        {
            return CloneWithResolvedMetadata(effectiveConfigPath, effectiveWorkspaceId);
        }

        if (isFallbackPreset && !ShouldApplyFallbackPreset(explicitOverrideKeys))
        {
            return CloneWithResolvedMetadata(effectiveConfigPath, effectiveWorkspaceId);
        }

        var swarmConfig = SwarmWorkspaceConfigLoader.LoadFromFile(resolvedConfigPath, environmentVariables);
        var fromSwarm = swarmConfig.ToDashboardOptions(
            string.IsNullOrWhiteSpace(effectiveWorkspaceId) ? null : effectiveWorkspaceId);

        return MergeWithSwarmOptions(fromSwarm, effectiveConfigPath, effectiveWorkspaceId, explicitOverrideKeys);
    }

    /// <summary>
    /// Resolves swarm configuration settings and applies the resulting effective values onto this instance.
    /// </summary>
    public void ApplySwarmConfigInPlace(
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        IReadOnlySet<string>? explicitOverrideKeys = null)
    {
        CopyFrom(ResolveWithSwarmConfig(environmentVariables, explicitOverrideKeys));
    }

    /// <summary>
    /// Captures <c>Dashboard:*</c> keys supplied by any configuration provider other than the shipped
    /// baseline <c>appsettings.json</c>, so explicit caller overrides equal to CLR/appsettings defaults
    /// (for example <c>GitHubProjectNumber = 1</c>) are never overwritten by <see cref="SwarmConfig"/>.
    /// </summary>
    public static IReadOnlySet<string> CaptureExplicitOverrideKeys(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var explicitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configuration is not IConfigurationRoot root)
        {
            CollectSectionKeys(configuration.GetSection(SectionName), explicitKeys);
            return explicitKeys;
        }

        foreach (var provider in root.Providers)
        {
            if (IsBaselineAppSettingsProvider(provider))
            {
                continue;
            }

            CollectProviderKeys(provider, explicitKeys);
        }

        return explicitKeys;
    }

    /// <summary>
    /// Copies all configurable properties from <paramref name="source"/> onto this instance.
    /// </summary>
    public void CopyFrom(DashboardOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        BindUrl = source.BindUrl;
        MobaDevRepoPath = source.MobaDevRepoPath;
        MobaFlowRepoPath = source.MobaFlowRepoPath;
        ClaudeHomePath = source.ClaudeHomePath;
        CodexHomePath = source.CodexHomePath;
        AntigravityHomePath = source.AntigravityHomePath;
        GeminiConfigPath = source.GeminiConfigPath;
        GitHubRepository = source.GitHubRepository;
        MonitoredRepositories = [.. source.MonitoredRepositories];
        GitHubProjectNumber = source.GitHubProjectNumber;
        GitHubProjectOwner = source.GitHubProjectOwner;
        LocalCacheTtlSeconds = source.LocalCacheTtlSeconds;
        GitHubCacheTtlSeconds = source.GitHubCacheTtlSeconds;
        BusyIdleAfterMinutes = source.BusyIdleAfterMinutes;
        StaticWebRootPath = source.StaticWebRootPath;
        SwarmBusDbPath = source.SwarmBusDbPath;
        SwarmConfigPath = source.SwarmConfigPath;
        WorkspaceId = source.WorkspaceId;
    }

    private DashboardOptions CloneWithResolvedMetadata(string effectiveConfigPath, string effectiveWorkspaceId)
    {
        var copy = new DashboardOptions();
        copy.CopyFrom(this);
        copy.SwarmConfigPath = effectiveConfigPath;
        copy.WorkspaceId = effectiveWorkspaceId;
        return copy;
    }

    private bool ShouldApplyFallbackPreset(IReadOnlySet<string>? explicitOverrideKeys)
    {
        if (HasExplicitOverride(explicitOverrideKeys, nameof(MobaFlowRepoPath)) ||
            HasExplicitOverride(explicitOverrideKeys, nameof(GitHubRepository)))
        {
            return false;
        }

        var defaults = new DashboardOptions();
        return string.IsNullOrWhiteSpace(MobaFlowRepoPath) &&
               string.Equals(GitHubRepository, defaults.GitHubRepository, StringComparison.Ordinal);
    }

    private DashboardOptions MergeWithSwarmOptions(
        DashboardOptions fromSwarm,
        string effectiveConfigPath,
        string effectiveWorkspaceId,
        IReadOnlySet<string>? explicitOverrideKeys)
    {
        var defaults = new DashboardOptions();

        return new DashboardOptions
        {
            BindUrl = ChooseStringOverride(BindUrl, fromSwarm.BindUrl, defaults.BindUrl, HasExplicitOverride(explicitOverrideKeys, nameof(BindUrl))),
            MobaDevRepoPath = ChoosePathOverride(MobaDevRepoPath, fromSwarm.MobaDevRepoPath),
            MobaFlowRepoPath = ChoosePathOverride(MobaFlowRepoPath, fromSwarm.MobaFlowRepoPath),
            ClaudeHomePath = ClaudeHomePath,
            CodexHomePath = CodexHomePath,
            AntigravityHomePath = AntigravityHomePath,
            GeminiConfigPath = GeminiConfigPath,
            GitHubRepository = ChooseStringOverride(GitHubRepository, fromSwarm.GitHubRepository, defaults.GitHubRepository, HasExplicitOverride(explicitOverrideKeys, nameof(GitHubRepository))),
            MonitoredRepositories = MonitoredRepositories.Length > 0 ? [.. MonitoredRepositories] : [.. fromSwarm.MonitoredRepositories],
            GitHubProjectNumber = ChooseIntOverride(GitHubProjectNumber, fromSwarm.GitHubProjectNumber, defaults.GitHubProjectNumber, HasExplicitOverride(explicitOverrideKeys, nameof(GitHubProjectNumber))),
            GitHubProjectOwner = ChooseStringOverride(GitHubProjectOwner, fromSwarm.GitHubProjectOwner, defaults.GitHubProjectOwner, HasExplicitOverride(explicitOverrideKeys, nameof(GitHubProjectOwner))),
            LocalCacheTtlSeconds = ChooseIntOverride(LocalCacheTtlSeconds, fromSwarm.LocalCacheTtlSeconds, defaults.LocalCacheTtlSeconds, HasExplicitOverride(explicitOverrideKeys, nameof(LocalCacheTtlSeconds))),
            GitHubCacheTtlSeconds = ChooseIntOverride(GitHubCacheTtlSeconds, fromSwarm.GitHubCacheTtlSeconds, defaults.GitHubCacheTtlSeconds, HasExplicitOverride(explicitOverrideKeys, nameof(GitHubCacheTtlSeconds))),
            BusyIdleAfterMinutes = BusyIdleAfterMinutes,
            StaticWebRootPath = ChoosePathOverride(StaticWebRootPath, fromSwarm.StaticWebRootPath),
            SwarmBusDbPath = ChoosePathOverride(SwarmBusDbPath, fromSwarm.SwarmBusDbPath),
            SwarmConfigPath = effectiveConfigPath,
            WorkspaceId = effectiveWorkspaceId
        };
    }

    private static bool IsBaselineAppSettingsProvider(IConfigurationProvider provider)
    {
        return provider is FileConfigurationProvider fileProvider &&
               string.Equals(Path.GetFileName(fileProvider.Source.Path), "appsettings.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectProviderKeys(IConfigurationProvider provider, HashSet<string> target)
    {
        foreach (var childKey in provider.GetChildKeys([], SectionName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (provider.TryGet($"{SectionName}:{childKey}", out var value) && !string.IsNullOrWhiteSpace(value))
            {
                target.Add(childKey);
            }
        }
    }

    private static void CollectSectionKeys(IConfigurationSection section, HashSet<string> target)
    {
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                target.Add(child.Key);
            }
        }
    }

    private static bool HasExplicitOverride(IReadOnlySet<string>? explicitOverrideKeys, string propertyName)
    {
        return explicitOverrideKeys is not null && explicitOverrideKeys.Contains(propertyName);
    }

    private static string ResolveOptionOrEnvironment(
        string configuredValue,
        string envVarName,
        IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue.Trim();
        }

        if (environmentVariables is not null)
        {
            var match = environmentVariables.FirstOrDefault(kv =>
                string.Equals(kv.Key, envVarName, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(match.Value) ? string.Empty : match.Value.Trim();
        }

        var envValue = Environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(envValue) ? string.Empty : envValue.Trim();
    }

    private static string? TryResolveSwarmConfigFilePath(
        string explicitConfigPath,
        string mobaDevRoot,
        out bool isFallbackPreset)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            isFallbackPreset = false;
            return Path.GetFullPath(explicitConfigPath);
        }

        if (!string.IsNullOrWhiteSpace(mobaDevRoot))
        {
            var localConfig = Path.Combine(mobaDevRoot, "swarm.config.json");
            if (File.Exists(localConfig))
            {
                isFallbackPreset = false;
                return localConfig;
            }

            var presetConfig = Path.Combine(mobaDevRoot, "presets", "mobaflow.swarm.json");
            if (File.Exists(presetConfig))
            {
                isFallbackPreset = true;
                return presetConfig;
            }
        }

        isFallbackPreset = false;
        return null;
    }

    private static string ChoosePathOverride(string configured, string fromSwarm)
    {
        return !string.IsNullOrWhiteSpace(configured) ? configured : fromSwarm;
    }

    private static string ChooseStringOverride(string configured, string fromSwarm, string defaultValue, bool isExplicit)
    {
        if (isExplicit && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(configured) &&
            !string.Equals(configured, defaultValue, StringComparison.Ordinal))
        {
            return configured;
        }

        return !string.IsNullOrWhiteSpace(fromSwarm) ? fromSwarm : defaultValue;
    }

    private static int ChooseIntOverride(int configured, int fromSwarm, int defaultValue, bool isExplicit)
    {
        if (isExplicit && configured > 0)
        {
            return configured;
        }

        if (configured != defaultValue && configured > 0)
        {
            return configured;
        }

        return fromSwarm > 0 ? fromSwarm : defaultValue;
    }

    /// <summary>
    /// Resolves the inactivity threshold after which a busy monitor transitions to idle.
    /// Non-positive values fall back to <see cref="DefaultBusyIdleAfterMinutes"/>.
    /// </summary>
    public TimeSpan ResolveBusyIdleThreshold()
    {
        var minutes = BusyIdleAfterMinutes >= 1 ? BusyIdleAfterMinutes : DefaultBusyIdleAfterMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

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
                : Path.Combine(mobaDevPath, "dashboard", "src", "AISwarmX.Dashboard.Web", "dist"));
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

    public IReadOnlyList<string> ResolveMonitoredRepositories()
    {
        IEnumerable<string> seeds = MonitoredRepositories.Length > 0
            ? MonitoredRepositories
            : [GitHubRepository, "ahuelsmann/SentiX", "ahuelsmann/AISwarmX"];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in seeds)
        {
            var trimmed = raw?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(GitHubRepository))
        {
            result.Add(GitHubRepository.Trim());
        }

        return result;
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
