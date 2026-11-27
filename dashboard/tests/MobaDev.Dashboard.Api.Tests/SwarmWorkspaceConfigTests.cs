using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MobaDev.Dashboard.Api.Configuration;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed partial class SwarmWorkspaceConfigTests
{
    [Fact]
    public void LoadFromFile_LoadsPresetConfigWithMultiProjectWorkspacesProfilesAndAgents()
    {
        var repoRoot = FindWorktreeRoot();
        var presetPath = Path.Combine(repoRoot, "presets", "mobaflow.swarm.json");

        var config = SwarmWorkspaceConfigLoader.LoadFromFile(
            presetPath,
            new Dictionary<string, string?>());

        Assert.Equal("1.0", config.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(config.SwarmName));
        Assert.Equal("mobaflow", config.DefaultWorkspaceId);
        Assert.Equal(repoRoot, config.BaseDirectory);

        Assert.Equal(2, config.Workspaces.Count);

        var mobaFlow = config.GetWorkspace();
        Assert.Equal("mobaflow", mobaFlow.Id);
        Assert.Equal("ahuelsmann/MOBAflow", mobaFlow.GitHubRepository);
        Assert.Equal("ahuelsmann", mobaFlow.GitHubProjectOwner);
        Assert.Equal(1, mobaFlow.GitHubProjectNumber);
        Assert.Equal(3, mobaFlow.CodeWipLimit);
        Assert.Equal(["docs/", "specs/", "plans/"], mobaFlow.LowRiskDocPrefixes);
        Assert.Equal(Path.Combine(repoRoot, "scripts", "Invoke-RepoHygiene.ps1"), mobaFlow.Scripts?.Hygiene);
        Assert.Equal(Path.Combine(repoRoot, "scripts", "Invoke-SwarmWatchdog.ps1"), mobaFlow.Scripts?.Watchdog);
        Assert.True(File.Exists(mobaFlow.Scripts?.Hygiene));
        Assert.True(File.Exists(mobaFlow.Scripts?.Watchdog));

        var mobaDev = config.GetWorkspace("mobadev");
        Assert.Equal("mobadev", mobaDev.Id);
        Assert.Equal("ahuelsmann/MOBAdev", mobaDev.GitHubRepository);
        Assert.Equal("ahuelsmann", mobaDev.GitHubProjectOwner);
        Assert.Equal(1, mobaDev.GitHubProjectNumber);
        Assert.Equal(2, mobaDev.CodeWipLimit);
        Assert.Equal(["docs/", "schemas/", "presets/", "CHANGELOG.md"], mobaDev.LowRiskDocPrefixes);

        Assert.Equal(4, config.CapabilityProfiles.Count);
        foreach (var profileKey in new[] { "P1", "P2", "P3", "P4" })
        {
            Assert.True(config.CapabilityProfiles.TryGetValue(profileKey, out var profile));
            Assert.NotNull(profile);
            Assert.NotEmpty(profile.AllowedModels);
            Assert.Contains("Claude Fable 5.2", profile.ProhibitedModels);
            Assert.Contains("claude-fable-5-2", profile.ProhibitedModels);
        }

        Assert.Equal(4, config.StandingAgents.Count);
        Assert.Contains(config.StandingAgents, a => a.Id == "claude-opus" && a.Family == "claude" && a.Profile == "P1" && a.WakeAdapter.Kind == "claude-ephemeral");
        Assert.Contains(config.StandingAgents, a => a.Id == "codex" && a.Family == "openai" && a.Profile == "P2" && a.WakeAdapter.Kind == "codex-queue");
        Assert.Contains(config.StandingAgents, a => a.Id == "antigravity-gemini" && a.Family == "gemini" && a.Profile == "P3" && a.WakeAdapter.Kind == "antigravity-http");
        Assert.Contains(config.StandingAgents, a => a.Id == "antigravity-opus" && a.Family == "claude" && a.Profile == "P1" && a.WakeAdapter.Kind == "antigravity-http");

        Assert.True(config.Bus.LockTtlMinutes >= 5);
        Assert.Equal("http://127.0.0.1:5179", config.Dashboard.BindUrl);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(repoRoot, "dashboard", "src", "MobaDev.Dashboard.Web", "dist")),
            config.Dashboard.StaticWebRootPath);
    }

    [Fact]
    public void Schema_StructuralPropertiesMatchPresetConfig()
    {
        var repoRoot = FindWorktreeRoot();
        var schemaPath = Path.Combine(repoRoot, "schemas", "swarm.config.schema.json");
        var presetPath = Path.Combine(repoRoot, "presets", "mobaflow.swarm.json");

        using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
        using var presetDoc = JsonDocument.Parse(File.ReadAllText(presetPath));

        var schemaRoot = schemaDoc.RootElement;
        var presetRoot = presetDoc.RootElement;

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schemaRoot.GetProperty("$schema").GetString());
        AssertObjectMatchesSchema(presetRoot, schemaRoot);

        // Workspaces array items
        var workspaceSchema = schemaRoot.GetProperty("properties").GetProperty("workspaces").GetProperty("items");
        var scriptsSchema = workspaceSchema.GetProperty("properties").GetProperty("scripts");
        foreach (var workspace in presetRoot.GetProperty("workspaces").EnumerateArray())
        {
            AssertObjectMatchesSchema(workspace, workspaceSchema);
            if (workspace.TryGetProperty("scripts", out var scripts))
            {
                AssertObjectMatchesSchema(scripts, scriptsSchema);
            }
        }

        // Capability profiles P1-P4
        var profilesSchema = schemaRoot.GetProperty("properties").GetProperty("capabilityProfiles");
        var profileDefSchema = schemaRoot.GetProperty("$defs").GetProperty("capabilityProfile");
        var presetProfiles = presetRoot.GetProperty("capabilityProfiles");
        AssertObjectMatchesSchema(presetProfiles, profilesSchema);
        foreach (var profileProp in presetProfiles.EnumerateObject())
        {
            AssertObjectMatchesSchema(profileProp.Value, profileDefSchema);
        }

        // Standing agents and wakeAdapter
        var agentSchema = schemaRoot.GetProperty("properties").GetProperty("standingAgents").GetProperty("items");
        var wakeAdapterSchema = agentSchema.GetProperty("properties").GetProperty("wakeAdapter");
        foreach (var agent in presetRoot.GetProperty("standingAgents").EnumerateArray())
        {
            AssertObjectMatchesSchema(agent, agentSchema);
            AssertObjectMatchesSchema(agent.GetProperty("wakeAdapter"), wakeAdapterSchema);
        }

        // Bus and dashboard sections
        AssertObjectMatchesSchema(presetRoot.GetProperty("bus"), schemaRoot.GetProperty("properties").GetProperty("bus"));
        AssertObjectMatchesSchema(presetRoot.GetProperty("dashboard"), schemaRoot.GetProperty("properties").GetProperty("dashboard"));
    }

    [Theory]
    [InlineData("presets/mobaflow.swarm.json")]
    [InlineData("schemas/swarm.config.schema.json")]
    public void RepositoryArtifacts_DoNotContainPersonalPathsOrGuidLiterals(string relativePath)
    {
        var repoRoot = FindWorktreeRoot();
        var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain(@"C:\Users\", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(GuidPatternRegex(), content);
    }

    [Fact]
    public void LoadFromFile_ExpandsEnvironmentVariablesAndSwitchesWorkspacesToDashboardOptions()
    {
        var repoRoot = FindWorktreeRoot();
        var presetPath = Path.Combine(repoRoot, "presets", "mobaflow.swarm.json");

        var rootFake = Path.Combine(Path.GetTempPath(), "synthetic-repos");
        var localAppFake = Path.Combine(Path.GetTempPath(), "synthetic-localappdata");
        var env = new Dictionary<string, string?>
        {
            ["SWARM_REPOS_ROOT"] = rootFake,
            ["LOCALAPPDATA"] = localAppFake,
            ["MOBADEV_CODEX_THREAD_ID"] = "thread-test-123",
            ["MOBADEV_CODEX_EXE"] = "codex.exe",
            ["MOBADEV_CLAUDE_EXE"] = "claude.exe",
            ["MOBADEV_ANTIGRAVITY_ENDPOINT"] = "http://127.0.0.1:4242"
        };

        var config = SwarmWorkspaceConfigLoader.LoadFromFile(presetPath, env);

        var mobaFlow = config.GetWorkspace("mobaflow");
        var mobaDev = config.GetWorkspace("mobadev");
        Assert.Equal(Path.GetFullPath($"{rootFake}/MOBAflow"), mobaFlow.RepoPath);
        Assert.Equal(Path.GetFullPath($"{rootFake}/MOBAdev"), mobaDev.RepoPath);
        Assert.Equal(Path.GetFullPath($"{localAppFake}/MOBAdev/swarm-bus.db"), config.Bus.DbPath);

        var codex = config.StandingAgents.Single(a => a.Id == "codex");
        Assert.Equal("thread-test-123", codex.WakeAdapter.ThreadIdOrEnv);
        Assert.Equal("codex.exe", codex.WakeAdapter.ExecutableOrEnv);
        Assert.Equal("http://127.0.0.1:4242", config.StandingAgents.Single(a => a.Id == "antigravity-gemini").WakeAdapter.EndpointOrEnv);

        var mobaDevOptions = config.ToDashboardOptions("mobadev");
        Assert.Equal("http://127.0.0.1:5179", mobaDevOptions.BindUrl);
        Assert.Equal("ahuelsmann/MOBAdev", mobaDevOptions.GitHubRepository);
        Assert.Equal("ahuelsmann", mobaDevOptions.GitHubProjectOwner);
        Assert.Equal(1, mobaDevOptions.GitHubProjectNumber);
        Assert.Equal(repoRoot, mobaDevOptions.MobaDevRepoPath);
        Assert.Equal(mobaDev.RepoPath, mobaDevOptions.MobaFlowRepoPath);
        Assert.Equal(config.Bus.DbPath, mobaDevOptions.SwarmBusDbPath);
        Assert.Equal(8, mobaDevOptions.LocalCacheTtlSeconds);
        Assert.Equal(45, mobaDevOptions.GitHubCacheTtlSeconds);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(repoRoot, "dashboard", "src", "MobaDev.Dashboard.Web", "dist")),
            mobaDevOptions.StaticWebRootPath);

        var defaultOptions = config.ToDashboardOptions();
        Assert.Equal("ahuelsmann/MOBAflow", defaultOptions.GitHubRepository);
        Assert.Equal(mobaFlow.RepoPath, defaultOptions.MobaFlowRepoPath);
    }

    [Fact]
    public void ToDashboardOptions_ArbitraryThirdWorkspaceMapsSelectedRepoPath()
    {
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));
        var customJson = presetJson.Replace(
            "\"id\": \"mobadev\"",
            "\"id\": \"third-app\"")
            .Replace(
            "\"repoPath\": \"${SWARM_REPOS_ROOT}/MOBAdev\"",
            "\"repoPath\": \"${SWARM_REPOS_ROOT}/ThirdApp\"")
            .Replace(
            "\"githubRepository\": \"ahuelsmann/MOBAdev\"",
            "\"githubRepository\": \"ahuelsmann/ThirdApp\"");

        var rootFake = Path.Combine(Path.GetTempPath(), "synthetic-repos-third");
        var env = new Dictionary<string, string?>
        {
            ["SWARM_REPOS_ROOT"] = rootFake,
            ["LOCALAPPDATA"] = Path.Combine(Path.GetTempPath(), "synthetic-localappdata-third")
        };

        var config = SwarmWorkspaceConfigLoader.Parse(customJson, repoRoot, env);
        var options = config.ToDashboardOptions("third-app");

        Assert.Equal("ahuelsmann/ThirdApp", options.GitHubRepository);
        Assert.Equal(Path.GetFullPath($"{rootFake}/ThirdApp"), options.MobaFlowRepoPath);
        Assert.Equal(repoRoot, options.MobaDevRepoPath);
    }

    [Fact]
    public void Parse_DoesNotExpandDisallowedMetadataFields()
    {
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));
        var modified = presetJson.Replace(
            "\"swarmName\": \"MOBAflow Agent Swarm\"",
            "\"swarmName\": \"${UNEXPANDED_SWARM_NAME}\"");

        var config = SwarmWorkspaceConfigLoader.Parse(
            modified,
            repoRoot,
            new Dictionary<string, string?> { ["UNEXPANDED_SWARM_NAME"] = "ShouldNotExpand" });

        Assert.Equal("${UNEXPANDED_SWARM_NAME}", config.SwarmName);
    }

    [Theory]
    [InlineData("\"defaultWorkspaceId\": \"mobaflow\"", "\"defaultWorkspaceId\": \"unknown-ws\"")]
    [InlineData("\"defaultWorkspaceId\": \"mobaflow\"", "\"defaultWorkspaceId\": \"\"")]
    [InlineData("\"id\": \"mobadev\"", "\"id\": \"mobaflow\"")]
    [InlineData("\"githubRepository\": \"ahuelsmann/MOBAdev\"", "\"githubRepository\": \"not-a-repo-slug\"")]
    [InlineData("\"bindUrl\": \"http://127.0.0.1:5179\"", "\"bindUrl\": \"http://0.0.0.0:5179\"")]
    [InlineData("\"bindUrl\": \"http://127.0.0.1:5179\"", "\"bindUrl\": \"${UNRESOLVED_BIND_URL}\"")]
    [InlineData("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"2.0\"")]
    [InlineData("\"codeWipLimit\": 3", "\"codeWipLimit\": 0")]
    [InlineData("\"codeWipLimit\": 3", "\"codeWipLimit\": 11")]
    [InlineData("\"lockTtlMinutes\": 30", "\"lockTtlMinutes\": 4")]
    [InlineData("\"swarmName\": \"MOBAflow Agent Swarm\"", "\"SwarmName\": \"MOBAflow Agent Swarm\"")]
    [InlineData("\"swarmName\": \"MOBAflow Agent Swarm\"", "\"swarmName\": \"MOBAflow Agent Swarm\", \"unexpectedProp\": true")]
    [InlineData("\"P4\": {", "\"P4_removed\": {")]
    [InlineData("\"Opus 5.5\"", "\"Claude Fable 5.2\"")]
    [InlineData("\"claude-fable-5-2\"", "\"claude-other-model\"")]
    [InlineData("\"repoPath\": \"${SWARM_REPOS_ROOT}/MOBAdev\"", "\"repoPath\": \"../escape/MOBAdev\"")]
    [InlineData("\"dbPath\": \"${LOCALAPPDATA}/MOBAdev/swarm-bus.db\"", "\"dbPath\": \"sub/../../swarm-bus.db\"")]
    [InlineData("\"staticWebRootPath\": \"dashboard/src/MobaDev.Dashboard.Web/dist\"", "\"staticWebRootPath\": \"../outside/dist\"")]
    [InlineData("\"hygiene\": \"scripts/Invoke-RepoHygiene.ps1\"", "\"hygiene\": \"../../evil.ps1\"")]
    [InlineData("\"threadIdOrEnv\": \"${MOBADEV_CODEX_THREAD_ID}\"", "\"threadIdOrEnv\": \"\"")]
    [InlineData("\"endpointOrEnv\": \"${MOBADEV_ANTIGRAVITY_ENDPOINT}\"", "\"endpointOrEnv\": \"http://192.168.1.50:4242\"")]
    public void Parse_ThrowsValidationExceptionOnInvalidReplacement(string target, string replacement)
    {
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));
        var invalidJson = presetJson.Replace(target, replacement);

        Assert.NotEqual(presetJson, invalidJson);
        Assert.Throws<SwarmConfigValidationException>(() =>
            SwarmWorkspaceConfigLoader.Parse(invalidJson, repoRoot, new Dictionary<string, string?>()));
    }

    [Fact]
    public void Parse_ThrowsValidationExceptionOnExpandedNonLoopbackBindUrlAndOverlappingModels()
    {
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));

        var envBindJson = presetJson.Replace("\"bindUrl\": \"http://127.0.0.1:5179\"", "\"bindUrl\": \"${TEST_BIND_URL}\"");
        Assert.Throws<SwarmConfigValidationException>(() =>
            SwarmWorkspaceConfigLoader.Parse(
                envBindJson,
                repoRoot,
                new Dictionary<string, string?> { ["TEST_BIND_URL"] = "http://0.0.0.0:5179" }));

        var overlapJson = presetJson.Replace(
            "\"prohibitedModels\": [\n        \"Claude Fable 5.2\",",
            "\"prohibitedModels\": [\n        \"Opus 5.5\",\n        \"Claude Fable 5.2\",")
            .Replace(
            "\"prohibitedModels\": [\r\n        \"Claude Fable 5.2\",",
            "\"prohibitedModels\": [\r\n        \"Opus 5.5\",\r\n        \"Claude Fable 5.2\",");

        Assert.NotEqual(presetJson, overlapJson);
        Assert.Throws<SwarmConfigValidationException>(() =>
            SwarmWorkspaceConfigLoader.Parse(overlapJson, repoRoot, new Dictionary<string, string?>()));
    }

    [Fact]
    public void DashboardOptions_ResolveWithSwarmConfig_SupportsExplicitPathEnvSwitchingAndOverrides()
    {
        var repoRoot = FindWorktreeRoot();
        var presetPath = Path.Combine(repoRoot, "presets", "mobaflow.swarm.json");
        var syntheticRepos = Path.Combine(Path.GetTempPath(), "synthetic-slice2-repos");
        var syntheticAppData = Path.Combine(Path.GetTempPath(), "synthetic-slice2-appdata");

        var env = new Dictionary<string, string?>
        {
            ["MOBADEV_SWARM_CONFIG"] = presetPath,
            ["MOBADEV_SWARM_WORKSPACE"] = "mobadev",
            ["SWARM_REPOS_ROOT"] = syntheticRepos,
            ["LOCALAPPDATA"] = syntheticAppData
        };

        var resolvedFromEnv = new DashboardOptions().ResolveWithSwarmConfig(env);
        Assert.Equal(presetPath, resolvedFromEnv.SwarmConfigPath);
        Assert.Equal("mobadev", resolvedFromEnv.WorkspaceId);
        Assert.Equal("ahuelsmann/MOBAdev", resolvedFromEnv.GitHubRepository);
        Assert.Equal(Path.GetFullPath($"{syntheticRepos}/MOBAdev"), resolvedFromEnv.MobaFlowRepoPath);
        Assert.Equal(repoRoot, resolvedFromEnv.MobaDevRepoPath);
        Assert.Equal(Path.GetFullPath($"{syntheticAppData}/MOBAdev/swarm-bus.db"), resolvedFromEnv.SwarmBusDbPath);

        // Explicit caller overrides must be preserved even when SwarmConfigPath is active
        var withOverrides = new DashboardOptions
        {
            SwarmConfigPath = presetPath,
            WorkspaceId = "mobadev",
            BindUrl = "http://localhost:5199",
            MobaFlowRepoPath = Path.Combine(syntheticRepos, "CustomOverride"),
            GitHubRepository = "custom-org/custom-repo",
            GitHubProjectNumber = 42,
            LocalCacheTtlSeconds = 15
        }.ResolveWithSwarmConfig(env);

        Assert.Equal("http://localhost:5199", withOverrides.BindUrl);
        Assert.Equal(Path.Combine(syntheticRepos, "CustomOverride"), withOverrides.MobaFlowRepoPath);
        Assert.Equal("custom-org/custom-repo", withOverrides.GitHubRepository);
        Assert.Equal(42, withOverrides.GitHubProjectNumber);
        Assert.Equal(15, withOverrides.LocalCacheTtlSeconds);
    }

    [Fact]
    public void DashboardOptions_ResolveWithSwarmConfig_AutoDiscoversLocalSwarmConfigAndPresetFallback()
    {
        using var fx = new TempDashboardFixture();
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));

        // Preserves explicit MobaFlowRepoPath when only presets/mobaflow.swarm.json fallback exists
        var explicitOnly = new DashboardOptions
        {
            MobaDevRepoPath = repoRoot,
            MobaFlowRepoPath = fx.MobaFlowPath
        }.ResolveWithSwarmConfig(new Dictionary<string, string?>());

        Assert.Equal(fx.MobaFlowPath, explicitOnly.MobaFlowRepoPath);
        Assert.Equal("ahuelsmann/MOBAflow", explicitOnly.GitHubRepository);

        // Auto-discovers swarm.config.json in MobaDevRepoPath
        File.WriteAllText(Path.Combine(fx.MobaDevPath, "swarm.config.json"), presetJson);
        var autoDiscovered = new DashboardOptions
        {
            MobaDevRepoPath = fx.MobaDevPath,
            WorkspaceId = "mobadev"
        }.ResolveWithSwarmConfig(new Dictionary<string, string?>());

        Assert.Equal("ahuelsmann/MOBAdev", autoDiscovered.GitHubRepository);
        Assert.Equal(fx.MobaDevPath, autoDiscovered.MobaDevRepoPath);
    }

    [Fact]
    public void DashboardOptions_CaptureExplicitOverrideKeys_PreservesValuesEqualToDefaults()
    {
        using var fx = new TempDashboardFixture();
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));
        var customJson = presetJson
            .Replace("\"githubProjectNumber\": 1", "\"githubProjectNumber\": 42")
            .Replace("\"bindUrl\": \"http://127.0.0.1:5179\"", "\"bindUrl\": \"http://localhost:5199\"");

        var configPath = Path.Combine(fx.RootPath, "custom.swarm.json");
        File.WriteAllText(configPath, customJson);

        // Baseline appsettings.json defines defaults (GitHubProjectNumber=1, BindUrl=127.0.0.1:5179);
        // an explicit in-memory override equal to those defaults MUST still take precedence over SwarmConfig.
        var baselineAppSettings = Path.Combine(fx.RootPath, "appsettings.json");
        File.WriteAllText(baselineAppSettings, """
            {
              "Dashboard": {
                "BindUrl": "http://127.0.0.1:5179",
                "GitHubRepository": "ahuelsmann/MOBAflow",
                "GitHubProjectNumber": 1,
                "GitHubProjectOwner": "ahuelsmann"
              }
            }
            """);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(baselineAppSettings, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dashboard:SwarmConfigPath"] = configPath,
                ["Dashboard:GitHubProjectNumber"] = "1",
                ["Dashboard:BindUrl"] = "http://127.0.0.1:5179"
            })
            .Build();

        var explicitKeys = DashboardOptions.CaptureExplicitOverrideKeys(configuration);
        var options = configuration.GetSection(DashboardOptions.SectionName).Get<DashboardOptions>()!
            .ResolveWithSwarmConfig(new Dictionary<string, string?>(), explicitKeys);

        Assert.Equal(1, options.GitHubProjectNumber);
        Assert.Equal("http://127.0.0.1:5179", options.BindUrl);

        // Without explicit override in higher-precedence provider, appsettings.json baseline yields to SwarmConfig (42 / 5199).
        var baselineOnlyConfig = new ConfigurationBuilder()
            .AddJsonFile(baselineAppSettings, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dashboard:SwarmConfigPath"] = configPath
            })
            .Build();

        var baselineExplicitKeys = DashboardOptions.CaptureExplicitOverrideKeys(baselineOnlyConfig);
        var fromSwarm = baselineOnlyConfig.GetSection(DashboardOptions.SectionName).Get<DashboardOptions>()!
            .ResolveWithSwarmConfig(new Dictionary<string, string?>(), baselineExplicitKeys);

        Assert.Equal(42, fromSwarm.GitHubProjectNumber);
        Assert.Equal("http://localhost:5199", fromSwarm.BindUrl);
    }

    private static void AssertObjectMatchesSchema(JsonElement instanceObj, JsonElement schemaObj)
    {
        Assert.False(schemaObj.GetProperty("additionalProperties").GetBoolean());

        var schemaProps = GetPropertyNames(schemaObj.GetProperty("properties"));
        var schemaRequired = schemaObj.TryGetProperty("required", out var reqEl)
            ? GetStringArray(reqEl)
            : [];

        foreach (var prop in instanceObj.EnumerateObject())
        {
            Assert.Contains(prop.Name, schemaProps);
        }

        foreach (var requiredProp in schemaRequired)
        {
            Assert.True(instanceObj.TryGetProperty(requiredProp, out _), $"Object missing required property '{requiredProp}'.");
        }
    }

    private static string FindWorktreeRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "schemas", "swarm.config.schema.json")) &&
                File.Exists(Path.Combine(current.FullName, "presets", "mobaflow.swarm.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate worktree root containing schemas/ and presets/.");
    }

    private static HashSet<string> GetPropertyNames(JsonElement propertiesObject)
    {
        return propertiesObject.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> GetStringArray(JsonElement arrayElement)
    {
        return arrayElement.EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPatternRegex();
}
