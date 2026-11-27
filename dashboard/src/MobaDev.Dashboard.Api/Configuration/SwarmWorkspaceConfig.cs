namespace MobaDev.Dashboard.Api.Configuration;

/// <summary>
/// Exception thrown when a swarm workspace configuration fails structural or semantic validation.
/// </summary>
public sealed class SwarmConfigValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="SwarmConfigValidationException"/> with a validation error message.
    /// </summary>
    public SwarmConfigValidationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SwarmConfigValidationException"/> with a message and inner exception.
    /// </summary>
    public SwarmConfigValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Root swarm workspace configuration contract loaded from <c>swarm.config.json</c> or a preset file.
/// </summary>
public sealed record SwarmConfig(
    string SchemaVersion,
    string SwarmName,
    string DefaultWorkspaceId,
    IReadOnlyList<WorkspaceDefinition> Workspaces,
    IReadOnlyDictionary<string, CapabilityProfileDefinition> CapabilityProfiles,
    IReadOnlyList<StandingAgentDefinition> StandingAgents,
    SwarmBusConfig Bus,
    DashboardEndpointConfig Dashboard)
{
    /// <summary>
    /// Returns the workspace matching <paramref name="workspaceId"/>, or the configured
    /// <see cref="DefaultWorkspaceId"/> workspace when <paramref name="workspaceId"/> is omitted or whitespace.
    /// </summary>
    /// <exception cref="SwarmConfigValidationException">
    /// Thrown when no workspace matches the requested or default identifier.
    /// </exception>
    public WorkspaceDefinition GetWorkspace(string? workspaceId = null)
    {
        var targetId = string.IsNullOrWhiteSpace(workspaceId)
            ? DefaultWorkspaceId
            : workspaceId.Trim();

        return FindWorkspaceOrNull(targetId)
            ?? throw new SwarmConfigValidationException(
                $"Workspace '{targetId}' was not found in swarm configuration '{SwarmName}'.");
    }

    /// <summary>
    /// Maps this swarm configuration and the selected workspace into <see cref="DashboardOptions"/>
    /// for consumption by dashboard providers and hosting setup.
    /// </summary>
    public DashboardOptions ToDashboardOptions(
        string? workspaceId = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        var selected = GetWorkspace(workspaceId);
        var mobaDevWorkspace = FindWorkspaceOrNull("mobadev");
        var mobaFlowWorkspace = FindWorkspaceOrNull("mobaflow");

        var mobaDevRepoPath = ResolveConcretePath(mobaDevWorkspace?.RepoPath, environmentVariables);
        var mobaFlowRepoPath = ResolveConcretePath(mobaFlowWorkspace?.RepoPath, environmentVariables);
        if (string.IsNullOrEmpty(mobaFlowRepoPath) &&
            !string.Equals(selected.Id, "mobadev", StringComparison.OrdinalIgnoreCase))
        {
            mobaFlowRepoPath = ResolveConcretePath(selected.RepoPath, environmentVariables);
        }

        var staticWebRootPath = ResolveConcretePath(Dashboard.StaticWebRootPath, environmentVariables);
        var swarmBusDbPath = ResolveConcretePath(Bus.DbPath, environmentVariables);

        return new DashboardOptions
        {
            BindUrl = Dashboard.BindUrl,
            MobaDevRepoPath = mobaDevRepoPath,
            MobaFlowRepoPath = mobaFlowRepoPath,
            GitHubRepository = selected.GitHubRepository,
            GitHubProjectOwner = selected.GitHubProjectOwner,
            GitHubProjectNumber = selected.GitHubProjectNumber,
            LocalCacheTtlSeconds = Dashboard.LocalCacheTtlSeconds,
            GitHubCacheTtlSeconds = Dashboard.GitHubCacheTtlSeconds,
            StaticWebRootPath = staticWebRootPath,
            SwarmBusDbPath = swarmBusDbPath
        };
    }

    private WorkspaceDefinition? FindWorkspaceOrNull(string workspaceId)
    {
        return Workspaces.FirstOrDefault(workspace =>
            string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveConcretePath(
        string? path,
        IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = environmentVariables is null
            ? path
            : SwarmWorkspaceConfigLoader.ExpandPlaceholders(path, environmentVariables);

        return SwarmWorkspaceConfigLoader.ContainsUnexpandedPlaceholder(expanded)
            ? string.Empty
            : expanded;
    }
}

/// <summary>
/// Configuration for an individual repository workspace managed by the swarm.
/// </summary>
public sealed record WorkspaceDefinition(
    string Id,
    string DisplayName,
    string RepoPath,
    string GitHubRepository,
    string GitHubProjectOwner,
    int GitHubProjectNumber,
    int CodeWipLimit,
    IReadOnlyList<string> LowRiskDocPrefixes,
    WorkspaceScriptsConfig? Scripts = null);

/// <summary>
/// Optional workspace automation script paths relative to the repository or workspace root.
/// </summary>
public sealed record WorkspaceScriptsConfig(
    string? Hygiene = null,
    string? Watchdog = null,
    string? QualityGates = null);

/// <summary>
/// Model allowlist and prohibition rules for a capability profile (<c>P1</c>–<c>P4</c>).
/// </summary>
public sealed record CapabilityProfileDefinition(
    string Description,
    IReadOnlyList<string> AllowedModels,
    IReadOnlyList<string> ProhibitedModels);

/// <summary>
/// Configuration for a standing head agent across Claude, OpenAI, or Gemini tool families.
/// </summary>
public sealed record StandingAgentDefinition(
    string Id,
    string DisplayName,
    string Family,
    string Profile,
    string Role,
    WakeAdapterConfig WakeAdapter);

/// <summary>
/// Wake/dispatch adapter metadata for reaching a standing agent.
/// </summary>
public sealed record WakeAdapterConfig(
    string Kind,
    string? ThreadIdOrEnv = null,
    string? ExecutableOrEnv = null,
    string? EndpointOrEnv = null);

/// <summary>
/// Swarm Bus SQLite coordination settings.
/// </summary>
public sealed record SwarmBusConfig(
    string DbPath,
    int LockTtlMinutes);

/// <summary>
/// Local dashboard endpoint and cache TTL settings.
/// </summary>
public sealed record DashboardEndpointConfig(
    string BindUrl,
    string StaticWebRootPath,
    int LocalCacheTtlSeconds,
    int GitHubCacheTtlSeconds);
