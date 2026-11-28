using AISwarmX.Dashboard.Api.Configuration;
using AISwarmX.Dashboard.Api.Providers;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

public sealed class ConfigDocsProviderTests
{
    [Fact]
    public async Task GetCatalogAndContent_ListsAllowListedDocsAndRejectsTraversal()
    {
        using var fx = new TempDashboardFixture();

        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "manifest.md"), "# MOBAdev Manifest\n\nSetup rules.\n");
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "CHANGELOG.md"), "# Changelog\n");
        var agyRulesDir = Path.Combine(fx.MobaDevPath, "agents", "antigravity", "rules");
        Directory.CreateDirectory(agyRulesDir);
        await File.WriteAllTextAsync(Path.Combine(agyRulesDir, "mobaflow-agent-network.md"), "# Network Rule\n");

        // Arbitrary non-allowlisted secret/markdown files must never appear in catalog or be readable
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, ".env"), "SECRET=123\n");
        await File.WriteAllTextAsync(Path.Combine(fx.MobaDevPath, "unlisted-notes.md"), "Private\n");

        var provider = new ConfigDocsProvider();
        var paths = new ResolvedDashboardPaths(
            fx.MobaDevPath, fx.MobaFlowPath, fx.ClaudeHomePath, fx.CodexHomePath,
            fx.AntigravityHomePath, fx.GeminiConfigPath, string.Empty);

        var aiswarmxCatalog = await provider.GetCatalogAsync(paths, "aiswarmx", CancellationToken.None);
        Assert.Equal("ok", aiswarmxCatalog.Source.Status);
        Assert.Equal("aiswarmx", aiswarmxCatalog.Scope);
        Assert.Equal("ahuelsmann/AISwarmX", aiswarmxCatalog.Repository);
        Assert.Contains(aiswarmxCatalog.Files, f => f.RelativePath == "manifest.md");
        Assert.Contains(aiswarmxCatalog.Files, f => f.RelativePath == "CHANGELOG.md");
        Assert.Contains(aiswarmxCatalog.Files, f => f.RelativePath == "agents/antigravity/rules/mobaflow-agent-network.md");
        Assert.DoesNotContain(aiswarmxCatalog.Files, f => f.FileName == "unlisted-notes.md");
        Assert.DoesNotContain(aiswarmxCatalog.Files, f => f.FileName == ".env");

        var legacyAliasCatalog = await provider.GetCatalogAsync(paths, "mobadev", CancellationToken.None);
        Assert.Equal("ok", legacyAliasCatalog.Source.Status);
        Assert.Equal("aiswarmx", legacyAliasCatalog.Scope);
        Assert.Equal("ahuelsmann/AISwarmX", legacyAliasCatalog.Repository);
        Assert.Contains(legacyAliasCatalog.Files, f => f.RelativePath == "manifest.md");

        var doc = await provider.GetDocumentContentAsync(paths, "aiswarmx", "manifest.md", CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal("aiswarmx", doc.Scope);
        Assert.Equal("ahuelsmann/AISwarmX", doc.Repository);
        Assert.Contains("# MOBAdev Manifest", doc.Content);
        Assert.False(doc.Truncated);

        var legacyDoc = await provider.GetDocumentContentAsync(paths, "mobadev", "manifest.md", CancellationToken.None);
        Assert.NotNull(legacyDoc);
        Assert.Equal("aiswarmx", legacyDoc.Scope);
        Assert.Equal("ahuelsmann/AISwarmX", legacyDoc.Repository);

        var unlisted = await provider.GetDocumentContentAsync(paths, "aiswarmx", "unlisted-notes.md", CancellationToken.None);
        Assert.Null(unlisted);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetDocumentContentAsync(paths, "aiswarmx", "../secret.md", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetCatalogAsync(paths, "mobaflow", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetCatalogAsync(paths, "other", CancellationToken.None));
    }
}
