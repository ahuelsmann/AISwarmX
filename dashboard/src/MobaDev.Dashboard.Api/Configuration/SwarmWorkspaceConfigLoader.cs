using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MobaDev.Dashboard.Api.Security;

namespace MobaDev.Dashboard.Api.Configuration;

/// <summary>
/// Loads, expands, and validates <see cref="SwarmConfig"/> documents against the MOBAdev workspace contract.
/// </summary>
public static partial class SwarmWorkspaceConfigLoader
{
    private const string SupportedSchemaVersion = "1.0";
    private const int DefaultCodeWipLimit = 3;
    private const int MinCodeWipLimit = 1;
    private const int MaxCodeWipLimit = 10;
    private const int MinLockTtlMinutes = 5;

    private static readonly string[] RequiredProhibitedFableModels =
    [
        "Claude Fable 5.2",
        "claude-fable-5-2"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private static readonly HashSet<string> AllowedFamilies = new(StringComparer.Ordinal)
    {
        "claude",
        "openai",
        "gemini"
    };

    private static readonly HashSet<string> AllowedProfiles = new(StringComparer.Ordinal)
    {
        "P1",
        "P2",
        "P3",
        "P4"
    };

    private static readonly HashSet<string> AllowedWakeAdapterKinds = new(StringComparer.Ordinal)
    {
        "none",
        "codex-queue",
        "claude-ephemeral",
        "antigravity-http"
    };

    /// <summary>
    /// Loads and validates a <see cref="SwarmConfig"/> from <paramref name="filePath"/>.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file.</param>
    /// <param name="environmentVariables">
    /// Optional environment variable map used exclusively for <c>${VAR_NAME}</c> placeholder expansion.
    /// When <c>null</c>, <see cref="Environment.GetEnvironmentVariable(string)"/> is used.
    /// </param>
    /// <param name="baseDirectory">
    /// Optional root directory for resolving relative paths. When <c>null</c>, preset files located under a
    /// <c>presets/</c> directory inside a MOBAdev root (containing <c>schemas/</c> or <c>manifest.md</c>)
    /// resolve relative paths against that parent root; all other config files use the file's directory.
    /// </param>
    public static SwarmConfig LoadFromFile(
        string filePath,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Configuration file path must not be empty.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Swarm configuration file not found: '{fullPath}'.", fullPath);
        }

        var json = File.ReadAllText(fullPath);
        var effectiveBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? ResolveDefaultBaseDirectory(Path.GetDirectoryName(fullPath)!)
            : Path.GetFullPath(baseDirectory);

        return Parse(json, effectiveBaseDirectory, environmentVariables);
    }

    /// <summary>
    /// Parses, expands <c>${VAR_NAME}</c> placeholders in, and validates a <see cref="SwarmConfig"/> JSON payload.
    /// </summary>
    /// <param name="json">Raw JSON configuration string.</param>
    /// <param name="baseDirectory">
    /// Optional base directory used to resolve relative filesystem paths when no unexpanded placeholders remain.
    /// </param>
    /// <param name="environmentVariables">
    /// Optional environment variable map used exclusively for <c>${VAR_NAME}</c> placeholder expansion.
    /// When <c>null</c>, <see cref="Environment.GetEnvironmentVariable(string)"/> is used.
    /// </param>
    /// <remarks>
    /// Only fields that support machine-local configuration (<c>repoPath</c>, <c>githubProjectOwner</c>,
    /// <c>scripts.*</c>, <c>wakeAdapter.*</c>, <c>bus.dbPath</c>, <c>dashboard.bindUrl</c>,
    /// <c>dashboard.staticWebRootPath</c>) undergo <c>${VAR_NAME}</c> expansion, in a single pass prior to
    /// path normalization and validation. Unresolved tokens are preserved for optional machine-local fields;
    /// unresolved tokens or non-loopback values in <c>dashboard.bindUrl</c> (or non-loopback literal
    /// <c>antigravity-http</c> endpoints) fail validation with <see cref="SwarmConfigValidationException"/>.
    /// </remarks>
    public static SwarmConfig Parse(
        string json,
        string? baseDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new SwarmConfigValidationException("Swarm configuration JSON must not be empty.");
        }

        SwarmConfigDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SwarmConfigDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new SwarmConfigValidationException($"Invalid swarm configuration JSON: {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new SwarmConfigValidationException("Swarm configuration JSON deserialized to null.");
        }

        var normalizedBaseDir = string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.GetFullPath(baseDirectory);

        var envLookup = CreateEnvLookup(environmentVariables);

        if (!string.Equals(dto.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new SwarmConfigValidationException(
                $"Unsupported schemaVersion '{dto.SchemaVersion}'. Expected '{SupportedSchemaVersion}'.");
        }

        var swarmName = RequireNonEmptyLiteral(dto.SwarmName, "swarmName");
        var defaultWorkspaceId = RequireSlug(dto.DefaultWorkspaceId, "defaultWorkspaceId");

        var workspaces = ParseWorkspaces(dto.Workspaces, normalizedBaseDir, envLookup);
        if (!workspaces.Any(w => string.Equals(w.Id, defaultWorkspaceId, StringComparison.Ordinal)))
        {
            throw new SwarmConfigValidationException(
                $"defaultWorkspaceId '{defaultWorkspaceId}' does not match any configured workspace.");
        }

        var capabilityProfiles = ParseCapabilityProfiles(dto.CapabilityProfiles);
        var standingAgents = ParseStandingAgents(dto.StandingAgents, capabilityProfiles, envLookup);
        var bus = ParseBus(dto.Bus, normalizedBaseDir, envLookup);
        var dashboard = ParseDashboard(dto.Dashboard, normalizedBaseDir, envLookup);

        return new SwarmConfig(
            dto.SchemaVersion!,
            swarmName,
            defaultWorkspaceId,
            workspaces,
            capabilityProfiles,
            standingAgents,
            bus,
            dashboard,
            normalizedBaseDir);
    }

    internal static bool ContainsUnexpandedPlaceholder(string? value)
    {
        return !string.IsNullOrEmpty(value) && PlaceholderRegex().IsMatch(value);
    }

    private static string ResolveDefaultBaseDirectory(string configDirectory)
    {
        var dirInfo = new DirectoryInfo(configDirectory);
        if (string.Equals(dirInfo.Name, "presets", StringComparison.OrdinalIgnoreCase) &&
            dirInfo.Parent is { } parent &&
            (File.Exists(Path.Combine(parent.FullName, "schemas", "swarm.config.schema.json")) ||
             File.Exists(Path.Combine(parent.FullName, "manifest.md"))))
        {
            return parent.FullName;
        }

        return dirInfo.FullName;
    }

    private static Func<string, string?> CreateEnvLookup(IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        if (environmentVariables is null)
        {
            return Environment.GetEnvironmentVariable;
        }

        var caseInsensitive = new Dictionary<string, string?>(environmentVariables, StringComparer.OrdinalIgnoreCase);
        return name => caseInsensitive.TryGetValue(name, out var value) ? value : null;
    }

    private static string ExpandPlaceholders(string value, Func<string, string?> envLookup)
    {
        return PlaceholderRegex().Replace(value, match =>
        {
            var varName = match.Groups[1].Value;
            var replacement = envLookup(varName);
            return replacement is null ? match.Value : replacement;
        });
    }

    private static string RequireNonEmptyLiteral(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new SwarmConfigValidationException($"Field '{fieldName}' is required and must not be empty.");
        }

        return raw.Trim();
    }

    private static string ExpandRequiredString(string? raw, string fieldName, Func<string, string?> envLookup)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new SwarmConfigValidationException($"Field '{fieldName}' is required and must not be empty.");
        }

        var expanded = ExpandPlaceholders(raw, envLookup).Trim();
        if (string.IsNullOrWhiteSpace(expanded))
        {
            throw new SwarmConfigValidationException($"Field '{fieldName}' must not expand to an empty string.");
        }

        return expanded;
    }

    private static string? ExpandOptionalString(string? raw, Func<string, string?> envLookup)
    {
        if (raw is null)
        {
            return null;
        }

        var expanded = ExpandPlaceholders(raw, envLookup).Trim();
        return string.IsNullOrEmpty(expanded) ? null : expanded;
    }

    private static string? ResolveOptionalConfiguredPath(
        string? raw,
        string fieldName,
        string? baseDirectory,
        bool confineRelativeToBaseDirectory,
        Func<string, string?> envLookup)
    {
        if (raw is null)
        {
            return null;
        }

        EnsureNoParentTraversal(raw, fieldName);
        var expanded = ExpandPlaceholders(raw, envLookup).Trim();
        if (string.IsNullOrEmpty(expanded))
        {
            return null;
        }

        EnsureNoParentTraversal(expanded, fieldName);
        return NormalizeConfiguredPath(expanded, fieldName, baseDirectory, confineRelativeToBaseDirectory);
    }

    private static string ResolveConfiguredPath(
        string? raw,
        string fieldName,
        bool allowEmpty,
        string? baseDirectory,
        bool confineRelativeToBaseDirectory,
        Func<string, string?> envLookup)
    {
        if (raw is null)
        {
            throw new SwarmConfigValidationException($"Field '{fieldName}' is required.");
        }

        EnsureNoParentTraversal(raw, fieldName);
        var expanded = ExpandPlaceholders(raw, envLookup).Trim();
        if (string.IsNullOrWhiteSpace(expanded))
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new SwarmConfigValidationException($"Field '{fieldName}' must not be empty.");
        }

        EnsureNoParentTraversal(expanded, fieldName);
        return NormalizeConfiguredPath(expanded, fieldName, baseDirectory, confineRelativeToBaseDirectory);
    }

    private static string NormalizeConfiguredPath(
        string expandedPath,
        string fieldName,
        string? baseDirectory,
        bool confineRelativeToBaseDirectory)
    {
        if (ContainsUnexpandedPlaceholder(expandedPath))
        {
            return expandedPath;
        }

        if (Path.IsPathRooted(expandedPath))
        {
            return Path.GetFullPath(expandedPath);
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return expandedPath;
        }

        var fullBase = Path.GetFullPath(baseDirectory);
        var resolved = Path.GetFullPath(Path.Combine(fullBase, expandedPath));
        if (confineRelativeToBaseDirectory && !IsWithinBaseDirectory(resolved, fullBase))
        {
            throw new SwarmConfigValidationException(
                $"Relative path '{expandedPath}' in '{fieldName}' must remain within '{fullBase}'.");
        }

        return resolved;
    }

    private static void EnsureNoParentTraversal(string path, string fieldName)
    {
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new SwarmConfigValidationException(
                $"Field '{fieldName}' value '{path}' must not contain parent directory traversal ('..').");
        }
    }

    private static bool IsWithinBaseDirectory(string candidateFullPath, string baseFullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var trimmedBase = baseFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = trimmedBase + Path.DirectorySeparatorChar;
        return string.Equals(candidateFullPath, trimmedBase, comparison) ||
               candidateFullPath.StartsWith(prefix, comparison);
    }

    private static string RequireSlug(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new SwarmConfigValidationException($"Field '{fieldName}' is required and must not be empty.");
        }

        var trimmed = raw.Trim();
        if (!SlugRegex().IsMatch(trimmed))
        {
            throw new SwarmConfigValidationException(
                $"Field '{fieldName}' value '{trimmed}' must match lowercase slug pattern ^[a-z0-9-]+$.");
        }

        return trimmed;
    }

    private static IReadOnlyList<WorkspaceDefinition> ParseWorkspaces(
        IReadOnlyList<WorkspaceDto?>? dtos,
        string? baseDirectory,
        Func<string, string?> envLookup)
    {
        if (dtos is null || dtos.Count == 0)
        {
            throw new SwarmConfigValidationException("At least one workspace must be configured in 'workspaces'.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WorkspaceDefinition>(dtos.Count);

        for (var i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i] ?? throw new SwarmConfigValidationException($"workspaces[{i}] must not be null.");
            var id = RequireSlug(dto.Id, $"workspaces[{i}].id");
            if (!seenIds.Add(id))
            {
                throw new SwarmConfigValidationException($"Duplicate workspace id '{id}' in 'workspaces'.");
            }

            var displayName = RequireNonEmptyLiteral(dto.DisplayName, $"workspaces[{i}].displayName");
            var repoPath = ResolveConfiguredPath(
                dto.RepoPath,
                $"workspaces[{i}].repoPath",
                allowEmpty: false,
                baseDirectory,
                confineRelativeToBaseDirectory: false,
                envLookup);

            var githubRepo = RequireNonEmptyLiteral(dto.GitHubRepository, $"workspaces[{i}].githubRepository");
            if (!GitHubRepoRegex().IsMatch(githubRepo))
            {
                throw new SwarmConfigValidationException(
                    $"Field 'workspaces[{i}].githubRepository' value '{githubRepo}' must be in 'owner/repo' format.");
            }

            var projectOwner = ExpandRequiredString(dto.GitHubProjectOwner, $"workspaces[{i}].githubProjectOwner", envLookup);
            if (dto.GitHubProjectNumber is null || dto.GitHubProjectNumber.Value < 1)
            {
                throw new SwarmConfigValidationException(
                    $"Field 'workspaces[{i}].githubProjectNumber' must be an integer >= 1.");
            }

            var wipLimit = dto.CodeWipLimit ?? DefaultCodeWipLimit;
            if (wipLimit < MinCodeWipLimit || wipLimit > MaxCodeWipLimit)
            {
                throw new SwarmConfigValidationException(
                    $"Field 'workspaces[{i}].codeWipLimit' value {wipLimit} must be between {MinCodeWipLimit} and {MaxCodeWipLimit}.");
            }

            var docPrefixes = ParseLiteralStringArray(dto.LowRiskDocPrefixes, $"workspaces[{i}].lowRiskDocPrefixes", allowMissing: true);
            WorkspaceScriptsConfig? scripts = null;
            if (dto.Scripts is not null)
            {
                scripts = new WorkspaceScriptsConfig(
                    ResolveOptionalConfiguredPath(
                        dto.Scripts.Hygiene,
                        $"workspaces[{i}].scripts.hygiene",
                        baseDirectory,
                        confineRelativeToBaseDirectory: true,
                        envLookup),
                    ResolveOptionalConfiguredPath(
                        dto.Scripts.Watchdog,
                        $"workspaces[{i}].scripts.watchdog",
                        baseDirectory,
                        confineRelativeToBaseDirectory: true,
                        envLookup),
                    ResolveOptionalConfiguredPath(
                        dto.Scripts.QualityGates,
                        $"workspaces[{i}].scripts.qualityGates",
                        baseDirectory,
                        confineRelativeToBaseDirectory: true,
                        envLookup));
            }

            result.Add(new WorkspaceDefinition(
                id,
                displayName,
                repoPath,
                githubRepo,
                projectOwner,
                dto.GitHubProjectNumber.Value,
                wipLimit,
                docPrefixes,
                scripts));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, CapabilityProfileDefinition> ParseCapabilityProfiles(
        CapabilityProfilesDto? dto)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException("Section 'capabilityProfiles' is required.");
        }

        return new Dictionary<string, CapabilityProfileDefinition>(StringComparer.Ordinal)
        {
            ["P1"] = ParseProfile(dto.P1, "capabilityProfiles.P1"),
            ["P2"] = ParseProfile(dto.P2, "capabilityProfiles.P2"),
            ["P3"] = ParseProfile(dto.P3, "capabilityProfiles.P3"),
            ["P4"] = ParseProfile(dto.P4, "capabilityProfiles.P4")
        };
    }

    private static CapabilityProfileDefinition ParseProfile(
        CapabilityProfileDto? dto,
        string pathPrefix)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException($"Capability profile '{pathPrefix}' is required.");
        }

        var description = RequireNonEmptyLiteral(dto.Description, $"{pathPrefix}.description");
        var allowedModels = ParseLiteralStringArray(dto.AllowedModels, $"{pathPrefix}.allowedModels", allowMissing: false);
        if (allowedModels.Count == 0)
        {
            throw new SwarmConfigValidationException($"Field '{pathPrefix}.allowedModels' must contain at least one model.");
        }

        var prohibitedModels = ParseLiteralStringArray(dto.ProhibitedModels, $"{pathPrefix}.prohibitedModels", allowMissing: false);

        var forbiddenAllowed = allowedModels.FirstOrDefault(
            allowed => allowed.Contains("fable", StringComparison.OrdinalIgnoreCase));
        if (forbiddenAllowed is not null)
        {
            throw new SwarmConfigValidationException(
                $"Field '{pathPrefix}.allowedModels' must not permit prohibited model '{forbiddenAllowed}'.");
        }

        var prohibitedSet = new HashSet<string>(prohibitedModels, StringComparer.OrdinalIgnoreCase);
        var missingBan = RequiredProhibitedFableModels.FirstOrDefault(
            requiredBan => !prohibitedSet.Contains(requiredBan));
        if (missingBan is not null)
        {
            throw new SwarmConfigValidationException(
                $"Field '{pathPrefix}.prohibitedModels' must explicitly prohibit '{missingBan}'.");
        }

        var overlappingModel = allowedModels.FirstOrDefault(prohibitedSet.Contains);
        if (overlappingModel is not null)
        {
            throw new SwarmConfigValidationException(
                $"Model '{overlappingModel}' in '{pathPrefix}' cannot appear in both 'allowedModels' and 'prohibitedModels'.");
        }

        return new CapabilityProfileDefinition(description, allowedModels, prohibitedModels);
    }

    private static IReadOnlyList<StandingAgentDefinition> ParseStandingAgents(
        IReadOnlyList<StandingAgentDto?>? dtos,
        IReadOnlyDictionary<string, CapabilityProfileDefinition> capabilityProfiles,
        Func<string, string?> envLookup)
    {
        if (dtos is null || dtos.Count == 0)
        {
            throw new SwarmConfigValidationException("At least one standing agent must be configured in 'standingAgents'.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<StandingAgentDefinition>(dtos.Count);
        for (var i = 0; i < dtos.Count; i++)
        {
            result.Add(ParseStandingAgent(dtos[i], i, capabilityProfiles, seenIds, envLookup));
        }

        return result;
    }

    private static StandingAgentDefinition ParseStandingAgent(
        StandingAgentDto? rawDto,
        int index,
        IReadOnlyDictionary<string, CapabilityProfileDefinition> capabilityProfiles,
        HashSet<string> seenIds,
        Func<string, string?> envLookup)
    {
        var dto = rawDto ?? throw new SwarmConfigValidationException($"standingAgents[{index}] must not be null.");
        var id = RequireSlug(dto.Id, $"standingAgents[{index}].id");
        if (!seenIds.Add(id))
        {
            throw new SwarmConfigValidationException($"Duplicate standing agent id '{id}' in 'standingAgents'.");
        }

        var displayName = RequireNonEmptyLiteral(dto.DisplayName, $"standingAgents[{index}].displayName");
        var family = RequireNonEmptyLiteral(dto.Family, $"standingAgents[{index}].family");
        if (!AllowedFamilies.Contains(family))
        {
            throw new SwarmConfigValidationException(
                $"Field 'standingAgents[{index}].family' value '{family}' must be one of: claude, openai, gemini.");
        }

        var profile = RequireNonEmptyLiteral(dto.Profile, $"standingAgents[{index}].profile");
        if (!AllowedProfiles.Contains(profile) || !capabilityProfiles.ContainsKey(profile))
        {
            throw new SwarmConfigValidationException(
                $"Field 'standingAgents[{index}].profile' value '{profile}' must be one of: P1, P2, P3, P4.");
        }

        var role = RequireNonEmptyLiteral(dto.Role, $"standingAgents[{index}].role");
        var wakeAdapter = ParseWakeAdapter(dto.WakeAdapter, index, envLookup);
        return new StandingAgentDefinition(id, displayName, family, profile, role, wakeAdapter);
    }

    private static WakeAdapterConfig ParseWakeAdapter(
        WakeAdapterDto? dto,
        int agentIndex,
        Func<string, string?> envLookup)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException($"Field 'standingAgents[{agentIndex}].wakeAdapter' is required.");
        }

        var adapterKind = RequireNonEmptyLiteral(dto.Kind, $"standingAgents[{agentIndex}].wakeAdapter.kind");
        if (!AllowedWakeAdapterKinds.Contains(adapterKind))
        {
            throw new SwarmConfigValidationException(
                $"Field 'standingAgents[{agentIndex}].wakeAdapter.kind' value '{adapterKind}' is invalid.");
        }

        var threadIdOrEnv = ExpandOptionalString(dto.ThreadIdOrEnv, envLookup);
        var executableOrEnv = ExpandOptionalString(dto.ExecutableOrEnv, envLookup);
        var endpointOrEnv = ExpandOptionalString(dto.EndpointOrEnv, envLookup);

        if (string.Equals(adapterKind, "codex-queue", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(threadIdOrEnv))
        {
            throw new SwarmConfigValidationException(
                $"Field 'standingAgents[{agentIndex}].wakeAdapter.threadIdOrEnv' is required for 'codex-queue' adapters.");
        }

        if (string.Equals(adapterKind, "antigravity-http", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(endpointOrEnv) &&
            !ContainsUnexpandedPlaceholder(endpointOrEnv))
        {
            endpointOrEnv = EnsureAdapterLoopbackEndpoint(endpointOrEnv, agentIndex);
        }

        return new WakeAdapterConfig(adapterKind, threadIdOrEnv, executableOrEnv, endpointOrEnv);
    }

    private static string EnsureAdapterLoopbackEndpoint(string endpoint, int agentIndex)
    {
        try
        {
            return LoopbackBindingGuard.EnsureLoopbackUrl(endpoint);
        }
        catch (InvalidOperationException ex)
        {
            throw new SwarmConfigValidationException(
                $"Field 'standingAgents[{agentIndex}].wakeAdapter.endpointOrEnv' must be a loopback URL: {ex.Message}",
                ex);
        }
    }

    private static SwarmBusConfig ParseBus(
        SwarmBusDto? dto,
        string? baseDirectory,
        Func<string, string?> envLookup)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException("Section 'bus' is required.");
        }

        var dbPath = ResolveConfiguredPath(
            dto.DbPath,
            "bus.dbPath",
            allowEmpty: false,
            baseDirectory,
            confineRelativeToBaseDirectory: false,
            envLookup);

        if (dto.LockTtlMinutes is null || dto.LockTtlMinutes.Value < MinLockTtlMinutes)
        {
            throw new SwarmConfigValidationException(
                $"Field 'bus.lockTtlMinutes' must be an integer >= {MinLockTtlMinutes}.");
        }

        return new SwarmBusConfig(dbPath, dto.LockTtlMinutes.Value);
    }

    private static DashboardEndpointConfig ParseDashboard(
        DashboardEndpointDto? dto,
        string? baseDirectory,
        Func<string, string?> envLookup)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException("Section 'dashboard' is required.");
        }

        var expandedBindUrl = ExpandRequiredString(dto.BindUrl, "dashboard.bindUrl", envLookup);
        string validatedBindUrl;
        try
        {
            validatedBindUrl = LoopbackBindingGuard.EnsureLoopbackUrl(expandedBindUrl);
        }
        catch (InvalidOperationException ex)
        {
            throw new SwarmConfigValidationException(ex.Message, ex);
        }

        var staticWebRootPath = ResolveConfiguredPath(
            dto.StaticWebRootPath,
            "dashboard.staticWebRootPath",
            allowEmpty: true,
            baseDirectory,
            confineRelativeToBaseDirectory: true,
            envLookup);

        if (dto.LocalCacheTtlSeconds is null || dto.LocalCacheTtlSeconds.Value < 1)
        {
            throw new SwarmConfigValidationException("Field 'dashboard.localCacheTtlSeconds' must be an integer >= 1.");
        }

        if (dto.GitHubCacheTtlSeconds is null || dto.GitHubCacheTtlSeconds.Value < 1)
        {
            throw new SwarmConfigValidationException("Field 'dashboard.githubCacheTtlSeconds' must be an integer >= 1.");
        }

        return new DashboardEndpointConfig(
            validatedBindUrl,
            staticWebRootPath,
            dto.LocalCacheTtlSeconds.Value,
            dto.GitHubCacheTtlSeconds.Value);
    }

    private static IReadOnlyList<string> ParseLiteralStringArray(
        IReadOnlyList<string?>? rawItems,
        string fieldName,
        bool allowMissing)
    {
        if (rawItems is null)
        {
            if (allowMissing)
            {
                return [];
            }

            throw new SwarmConfigValidationException($"Field '{fieldName}' is required.");
        }

        var items = new List<string>(rawItems.Count);
        for (var i = 0; i < rawItems.Count; i++)
        {
            items.Add(RequireNonEmptyLiteral(rawItems[i], $"{fieldName}[{i}]"));
        }

        return items;
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"^[^/\s]+/[^/\s]+$")]
    private static partial Regex GitHubRepoRegex();

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class SwarmConfigDto
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; set; }

        [JsonPropertyName("swarmName")]
        public string? SwarmName { get; set; }

        [JsonPropertyName("defaultWorkspaceId")]
        public string? DefaultWorkspaceId { get; set; }

        [JsonPropertyName("workspaces")]
        public List<WorkspaceDto?>? Workspaces { get; set; }

        [JsonPropertyName("capabilityProfiles")]
        public CapabilityProfilesDto? CapabilityProfiles { get; set; }

        [JsonPropertyName("standingAgents")]
        public List<StandingAgentDto?>? StandingAgents { get; set; }

        [JsonPropertyName("bus")]
        public SwarmBusDto? Bus { get; set; }

        [JsonPropertyName("dashboard")]
        public DashboardEndpointDto? Dashboard { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class WorkspaceDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("repoPath")]
        public string? RepoPath { get; set; }

        [JsonPropertyName("githubRepository")]
        public string? GitHubRepository { get; set; }

        [JsonPropertyName("githubProjectOwner")]
        public string? GitHubProjectOwner { get; set; }

        [JsonPropertyName("githubProjectNumber")]
        public int? GitHubProjectNumber { get; set; }

        [JsonPropertyName("codeWipLimit")]
        public int? CodeWipLimit { get; set; }

        [JsonPropertyName("lowRiskDocPrefixes")]
        public List<string?>? LowRiskDocPrefixes { get; set; }

        [JsonPropertyName("scripts")]
        public WorkspaceScriptsDto? Scripts { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class WorkspaceScriptsDto
    {
        [JsonPropertyName("hygiene")]
        public string? Hygiene { get; set; }

        [JsonPropertyName("watchdog")]
        public string? Watchdog { get; set; }

        [JsonPropertyName("qualityGates")]
        public string? QualityGates { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class CapabilityProfilesDto
    {
        [JsonPropertyName("P1")]
        public CapabilityProfileDto? P1 { get; set; }

        [JsonPropertyName("P2")]
        public CapabilityProfileDto? P2 { get; set; }

        [JsonPropertyName("P3")]
        public CapabilityProfileDto? P3 { get; set; }

        [JsonPropertyName("P4")]
        public CapabilityProfileDto? P4 { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class CapabilityProfileDto
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("allowedModels")]
        public List<string?>? AllowedModels { get; set; }

        [JsonPropertyName("prohibitedModels")]
        public List<string?>? ProhibitedModels { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class StandingAgentDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("family")]
        public string? Family { get; set; }

        [JsonPropertyName("profile")]
        public string? Profile { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("wakeAdapter")]
        public WakeAdapterDto? WakeAdapter { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class WakeAdapterDto
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("threadIdOrEnv")]
        public string? ThreadIdOrEnv { get; set; }

        [JsonPropertyName("executableOrEnv")]
        public string? ExecutableOrEnv { get; set; }

        [JsonPropertyName("endpointOrEnv")]
        public string? EndpointOrEnv { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class SwarmBusDto
    {
        [JsonPropertyName("dbPath")]
        public string? DbPath { get; set; }

        [JsonPropertyName("lockTtlMinutes")]
        public int? LockTtlMinutes { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class DashboardEndpointDto
    {
        [JsonPropertyName("bindUrl")]
        public string? BindUrl { get; set; }

        [JsonPropertyName("staticWebRootPath")]
        public string? StaticWebRootPath { get; set; }

        [JsonPropertyName("localCacheTtlSeconds")]
        public int? LocalCacheTtlSeconds { get; set; }

        [JsonPropertyName("githubCacheTtlSeconds")]
        public int? GitHubCacheTtlSeconds { get; set; }
    }
}
