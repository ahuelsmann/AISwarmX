using System.Text.Json;
using MobaDev.Dashboard.Api.Configuration;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed class SwarmWorkspaceConfigTests
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

        Assert.Equal(2, config.Workspaces.Count);

        var mobaFlow = config.GetWorkspace();
        Assert.Equal("mobaflow", mobaFlow.Id);
        Assert.Equal("ahuelsmann/MOBAflow", mobaFlow.GitHubRepository);
        Assert.Equal("ahuelsmann", mobaFlow.GitHubProjectOwner);
        Assert.Equal(1, mobaFlow.GitHubProjectNumber);
        Assert.Equal(3, mobaFlow.CodeWipLimit);
        Assert.Equal(["docs/", "specs/", "plans/"], mobaFlow.LowRiskDocPrefixes);
        Assert.Equal("scripts/Invoke-RepoHygiene.ps1", mobaFlow.Scripts?.Hygiene);
        Assert.Equal("scripts/Invoke-SwarmWatchdog.ps1", mobaFlow.Scripts?.Watchdog);

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
        Assert.False(schemaRoot.GetProperty("additionalProperties").GetBoolean());

        var schemaRootProps = GetPropertyNames(schemaRoot.GetProperty("properties"));
        var schemaRootRequired = GetStringArray(schemaRoot.GetProperty("required"));
        foreach (var presetProp in presetRoot.EnumerateObject())
        {
            Assert.Contains(presetProp.Name, schemaRootProps);
        }

        foreach (var requiredProp in schemaRootRequired)
        {
            Assert.True(presetRoot.TryGetProperty(requiredProp, out _), $"Preset missing required root property '{requiredProp}'.");
        }

        var workspaceSchema = schemaRoot.GetProperty("properties").GetProperty("workspaces").GetProperty("items");
        Assert.False(workspaceSchema.GetProperty("additionalProperties").GetBoolean());

        var workspaceSchemaProps = GetPropertyNames(workspaceSchema.GetProperty("properties"));
        var workspaceRequired = GetStringArray(workspaceSchema.GetProperty("required"));

        foreach (var workspace in presetRoot.GetProperty("workspaces").EnumerateArray())
        {
            foreach (var prop in workspace.EnumerateObject())
            {
                Assert.Contains(prop.Name, workspaceSchemaProps);
            }

            foreach (var requiredProp in workspaceRequired)
            {
                Assert.True(workspace.TryGetProperty(requiredProp, out _), $"Workspace missing required property '{requiredProp}'.");
            }
        }
    }

    [Theory]
    [InlineData("presets/mobaflow.swarm.json")]
    [InlineData("schemas/swarm.config.schema.json")]
    public void RepositoryArtifacts_DoNotContainPersonalPathsOrThreadUuids(string relativePath)
    {
        var repoRoot = FindWorktreeRoot();
        var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain(@"C:\Users\", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("01a14504-bd6c-7a50-b715-1a2f2ec76467", content, StringComparison.OrdinalIgnoreCase);
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

        var mobaDevOptions = config.ToDashboardOptions("mobadev", env);
        Assert.Equal("http://127.0.0.1:5179", mobaDevOptions.BindUrl);
        Assert.Equal("ahuelsmann/MOBAdev", mobaDevOptions.GitHubRepository);
        Assert.Equal("ahuelsmann", mobaDevOptions.GitHubProjectOwner);
        Assert.Equal(1, mobaDevOptions.GitHubProjectNumber);
        Assert.Equal(mobaDev.RepoPath, mobaDevOptions.MobaDevRepoPath);
        Assert.Equal(mobaFlow.RepoPath, mobaDevOptions.MobaFlowRepoPath);
        Assert.Equal(config.Bus.DbPath, mobaDevOptions.SwarmBusDbPath);
        Assert.Equal(8, mobaDevOptions.LocalCacheTtlSeconds);
        Assert.Equal(45, mobaDevOptions.GitHubCacheTtlSeconds);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(presetPath)!, "dashboard", "src", "MobaDev.Dashboard.Web", "dist")),
            mobaDevOptions.StaticWebRootPath);

        var defaultOptions = config.ToDashboardOptions(environmentVariables: env);
        Assert.Equal("ahuelsmann/MOBAflow", defaultOptions.GitHubRepository);
    }

    [Fact]
    public void Parse_ThrowsValidationExceptionOnInvalidConfigurations()
    {
        var repoRoot = FindWorktreeRoot();
        var presetJson = File.ReadAllText(Path.Combine(repoRoot, "presets", "mobaflow.swarm.json"));

        var missingDefaultWorkspace = presetJson.Replace("\"defaultWorkspaceId\": \"mobaflow\"", "\"defaultWorkspaceId\": \"unknown-ws\"");
        Assert.Throws<SwarmConfigValidationException>(() => SwarmWorkspaceConfigLoader.Parse(missingDefaultWorkspace));

        var blankDefaultWorkspace = presetJson.Replace("\"defaultWorkspaceId\": \"mobaflow\"", "\"defaultWorkspaceId\": \"\"");
        Assert.Throws<SwarmConfigValidationException>(() => SwarmWorkspaceConfigLoader.Parse(blankDefaultWorkspace));

        var duplicateWorkspaces = presetJson.Replace("\"id\": \"mobadev\"", "\"id\": \"mobaflow\"");
        Assert.Throws<SwarmConfigValidationException>(() => SwarmWorkspaceConfigLoader.Parse(duplicateWorkspaces));

        var invalidGitHubRepo = presetJson.Replace("\"githubRepository\": \"ahuelsmann/MOBAdev\"", "\"githubRepository\": \"not-a-repo-slug\"");
        Assert.Throws<SwarmConfigValidationException>(() => SwarmWorkspaceConfigLoader.Parse(invalidGitHubRepo));

        var nonLoopbackBindUrl = presetJson.Replace("\"bindUrl\": \"http://127.0.0.1:5179\"", "\"bindUrl\": \"http://0.0.0.0:5179\"");
        Assert.Throws<SwarmConfigValidationException>(() => SwarmWorkspaceConfigLoader.Parse(nonLoopbackBindUrl));

        var envExpandedNonLoopback = presetJson.Replace("\"bindUrl\": \"http://127.0.0.1:5179\"", "\"bindUrl\": \"${TEST_BIND_URL}\"");
        Assert.Throws<SwarmConfigValidationException>(() =>
            SwarmWorkspaceConfigLoader.Parse(
                envExpandedNonLoopback,
                environmentVariables: new Dictionary<string, string?> { ["TEST_BIND_URL"] = "http://0.0.0.0:5179" }));
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
}
