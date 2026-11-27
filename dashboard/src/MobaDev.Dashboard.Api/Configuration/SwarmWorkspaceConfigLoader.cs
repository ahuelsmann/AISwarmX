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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
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
    /// Loads and validates a <see cref="SwarmConfig"/> from <paramref name="filePath"/>, resolving relative
    /// filesystem paths against the file's directory.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file.</param>
    /// <param name="environmentVariables">
    /// Optional environment variable map used exclusively for <c>${VAR_NAME}</c> placeholder expansion.
    /// When <c>null</c>, <see cref="Environment.GetEnvironmentVariable(string)"/> is used.
    /// </param>
    public static SwarmConfig LoadFromFile(
        string filePath,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
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
        var baseDirectory = Path.GetDirectoryName(fullPath);
        return Parse(json, baseDirectory, environmentVariables);
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
    /// Unresolved <c>${VAR_NAME}</c> tokens are preserved verbatim for optional machine-local placeholders (such as
    /// repository root or adapter thread variables); if a placeholder is used in <c>dashboard.bindUrl</c> and remains
    /// unresolved or expands to a non-loopback address, validation fails with <see cref="SwarmConfigValidationException"/>.
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

        var envLookup = CreateEnvLookup(environmentVariables);

        if (!string.Equals(dto.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new SwarmConfigValidationException(
                $"Unsupported schemaVersion '{dto.SchemaVersion}'. Expected '{SupportedSchemaVersion}'.");
        }

        var swarmName = ExpandRequiredString(dto.SwarmName, "swarmName", envLookup);
        var defaultWorkspaceId = RequireSlug(dto.DefaultWorkspaceId, "defaultWorkspaceId");

        var workspaces = ParseWorkspaces(dto.Workspaces, baseDirectory, envLookup);
        if (!workspaces.Any(w => string.Equals(w.Id, defaultWorkspaceId, StringComparison.Ordinal)))
        {
            throw new SwarmConfigValidationException(
                $"defaultWorkspaceId '{defaultWorkspaceId}' does not match any configured workspace.");
        }

        var capabilityProfiles = ParseCapabilityProfiles(dto.CapabilityProfiles, envLookup);
        var standingAgents = ParseStandingAgents(dto.StandingAgents, capabilityProfiles, envLookup);
        var bus = ParseBus(dto.Bus, baseDirectory, envLookup);
        var dashboard = ParseDashboard(dto.Dashboard, baseDirectory, envLookup);

        return new SwarmConfig(
            dto.SchemaVersion!,
            swarmName,
            defaultWorkspaceId,
            workspaces,
            capabilityProfiles,
            standingAgents,
            bus,
            dashboard);
    }

    internal static string ExpandPlaceholders(string value, IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        return ExpandPlaceholders(value, CreateEnvLookup(environmentVariables));
    }

    internal static bool ContainsUnexpandedPlaceholder(string? value)
    {
        return !string.IsNullOrEmpty(value) && PlaceholderRegex().IsMatch(value);
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

    private static string ResolveConfiguredPath(
        string? raw,
        string fieldName,
        bool allowEmpty,
        string? baseDirectory,
        Func<string, string?> envLookup)
    {
        if (raw is null)
        {
            throw new SwarmConfigValidationException($"Field '{fieldName}' is required.");
        }

        var expanded = ExpandPlaceholders(raw, envLookup).Trim();
        if (string.IsNullOrWhiteSpace(expanded))
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new SwarmConfigValidationException($"Field '{fieldName}' must not be empty.");
        }

        if (ContainsUnexpandedPlaceholder(expanded))
        {
            return expanded;
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }

        return expanded;
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

            var displayName = ExpandRequiredString(dto.DisplayName, $"workspaces[{i}].displayName", envLookup);
            var repoPath = ResolveConfiguredPath(dto.RepoPath, $"workspaces[{i}].repoPath", allowEmpty: false, baseDirectory, envLookup);
            var githubRepo = ExpandRequiredString(dto.GitHubRepository, $"workspaces[{i}].githubRepository", envLookup);
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

            var docPrefixes = ParseStringArray(dto.LowRiskDocPrefixes, $"workspaces[{i}].lowRiskDocPrefixes", allowMissing: true, envLookup);
            WorkspaceScriptsConfig? scripts = null;
            if (dto.Scripts is not null)
            {
                scripts = new WorkspaceScriptsConfig(
                    ExpandOptionalString(dto.Scripts.Hygiene, envLookup),
                    ExpandOptionalString(dto.Scripts.Watchdog, envLookup),
                    ExpandOptionalString(dto.Scripts.QualityGates, envLookup));
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
        CapabilityProfilesDto? dto,
        Func<string, string?> envLookup)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException("Section 'capabilityProfiles' is required.");
        }

        return new Dictionary<string, CapabilityProfileDefinition>(StringComparer.Ordinal)
        {
            ["P1"] = ParseProfile(dto.P1, "capabilityProfiles.P1", envLookup),
            ["P2"] = ParseProfile(dto.P2, "capabilityProfiles.P2", envLookup),
            ["P3"] = ParseProfile(dto.P3, "capabilityProfiles.P3", envLookup),
            ["P4"] = ParseProfile(dto.P4, "capabilityProfiles.P4", envLookup)
        };
    }

    private static CapabilityProfileDefinition ParseProfile(
        CapabilityProfileDto? dto,
        string pathPrefix,
        Func<string, string?> envLookup)
    {
        if (dto is null)
        {
            throw new SwarmConfigValidationException($"Capability profile '{pathPrefix}' is required.");
        }

        var description = ExpandRequiredString(dto.Description, $"{pathPrefix}.description", envLookup);
        var allowedModels = ParseStringArray(dto.AllowedModels, $"{pathPrefix}.allowedModels", allowMissing: false, envLookup);
        if (allowedModels.Count == 0)
        {
            throw new SwarmConfigValidationException($"Field '{pathPrefix}.allowedModels' must contain at least one model.");
        }

        var prohibitedModels = ParseStringArray(dto.ProhibitedModels, $"{pathPrefix}.prohibitedModels", allowMissing: false, envLookup);
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
            var dto = dtos[i] ?? throw new SwarmConfigValidationException($"standingAgents[{i}] must not be null.");
            var id = RequireSlug(dto.Id, $"standingAgents[{i}].id");
            if (!seenIds.Add(id))
            {
                throw new SwarmConfigValidationException($"Duplicate standing agent id '{id}' in 'standingAgents'.");
            }

            var displayName = ExpandRequiredString(dto.DisplayName, $"standingAgents[{i}].displayName", envLookup);
            var family = ExpandRequiredString(dto.Family, $"standingAgents[{i}].family", envLookup);
            if (!AllowedFamilies.Contains(family))
            {
                throw new SwarmConfigValidationException(
                    $"Field 'standingAgents[{i}].family' value '{family}' must be one of: claude, openai, gemini.");
            }

            var profile = ExpandRequiredString(dto.Profile, $"standingAgents[{i}].profile", envLookup);
            if (!AllowedProfiles.Contains(profile) || !capabilityProfiles.ContainsKey(profile))
            {
                throw new SwarmConfigValidationException(
                    $"Field 'standingAgents[{i}].profile' value '{profile}' must be one of: P1, P2, P3, P4.");
            }

            var role = ExpandRequiredString(dto.Role, $"standingAgents[{i}].role", envLookup);
            if (dto.WakeAdapter is null)
            {
                throw new SwarmConfigValidationException($"Field 'standingAgents[{i}].wakeAdapter' is required.");
            }

            var adapterKind = ExpandRequiredString(dto.WakeAdapter.Kind, $"standingAgents[{i}].wakeAdapter.kind", envLookup);
            if (!AllowedWakeAdapterKinds.Contains(adapterKind))
            {
                throw new SwarmConfigValidationException(
                    $"Field 'standingAgents[{i}].wakeAdapter.kind' value '{adapterKind}' is invalid.");
            }

            var wakeAdapter = new WakeAdapterConfig(
                adapterKind,
                ExpandOptionalString(dto.WakeAdapter.ThreadIdOrEnv, envLookup),
                ExpandOptionalString(dto.WakeAdapter.ExecutableOrEnv, envLookup),
                ExpandOptionalString(dto.WakeAdapter.EndpointOrEnv, envLookup));

            result.Add(new StandingAgentDefinition(id, displayName, family, profile, role, wakeAdapter));
        }

        return result;
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

        var dbPath = ResolveConfiguredPath(dto.DbPath, "bus.dbPath", allowEmpty: false, baseDirectory, envLookup);
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

    private static IReadOnlyList<string> ParseStringArray(
        IReadOnlyList<string?>? rawItems,
        string fieldName,
        bool allowMissing,
        Func<string, string?> envLookup)
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
            items.Add(ExpandRequiredString(rawItems[i], $"{fieldName}[{i}]", envLookup));
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

        public string? SchemaVersion { get; set; }
        public string? SwarmName { get; set; }
        public string? DefaultWorkspaceId { get; set; }
        public List<WorkspaceDto?>? Workspaces { get; set; }
        public CapabilityProfilesDto? CapabilityProfiles { get; set; }
        public List<StandingAgentDto?>? StandingAgents { get; set; }
        public SwarmBusDto? Bus { get; set; }
        public DashboardEndpointDto? Dashboard { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class WorkspaceDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? RepoPath { get; set; }
        public string? GitHubRepository { get; set; }
        public string? GitHubProjectOwner { get; set; }
        public int? GitHubProjectNumber { get; set; }
        public int? CodeWipLimit { get; set; }
        public List<string?>? LowRiskDocPrefixes { get; set; }
        public WorkspaceScriptsDto? Scripts { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class WorkspaceScriptsDto
    {
        public string? Hygiene { get; set; }
        public string? Watchdog { get; set; }
        public string? QualityGates { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class CapabilityProfilesDto
    {
        public CapabilityProfileDto? P1 { get; set; }
        public CapabilityProfileDto? P2 { get; set; }
        public CapabilityProfileDto? P3 { get; set; }
        public CapabilityProfileDto? P4 { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class CapabilityProfileDto
    {
        public string? Description { get; set; }
        public List<string?>? AllowedModels { get; set; }
        public List<string?>? ProhibitedModels { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class StandingAgentDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Family { get; set; }
        public string? Profile { get; set; }
        public string? Role { get; set; }
        public WakeAdapterDto? WakeAdapter { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class WakeAdapterDto
    {
        public string? Kind { get; set; }
        public string? ThreadIdOrEnv { get; set; }
        public string? ExecutableOrEnv { get; set; }
        public string? EndpointOrEnv { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class SwarmBusDto
    {
        public string? DbPath { get; set; }
        public int? LockTtlMinutes { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class DashboardEndpointDto
    {
        public string? BindUrl { get; set; }
        public string? StaticWebRootPath { get; set; }
        public int? LocalCacheTtlSeconds { get; set; }
        public int? GitHubCacheTtlSeconds { get; set; }
    }
}
