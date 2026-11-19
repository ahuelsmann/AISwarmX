using MobaDev.Dashboard.Api.Security;
using Xunit;

namespace MobaDev.Dashboard.Api.Tests;

public sealed class PathWhitelistGuardTests
{
    [Theory]
    [InlineData(@"C:\synthetic\.codex\auth.json")]
    [InlineData(@"C:\synthetic\.claude\.credentials.json")]
    [InlineData(@"C:\synthetic\.gemini\oauth_creds.json")]
    [InlineData(@"C:\synthetic\.gemini\google_accounts.json")]
    [InlineData(@"C:\synthetic\.gemini\jetski-standalone-oauth-token")]
    [InlineData(@"C:\synthetic\repo\.env")]
    [InlineData(@"C:\synthetic\repo\.env.local")]
    [InlineData(@"C:\synthetic\certs\dev.key")]
    [InlineData(@"C:\synthetic\certs\dev.pem")]
    [InlineData(@"C:\synthetic\certs\dev.pfx")]
    [InlineData(@"C:\synthetic\config\my_secret_settings.json")]
    public void IsAllowedFilePath_BlocksCredentialAndSecretFiles(string path)
    {
        Assert.False(PathWhitelistGuard.IsAllowedFilePath(path));
        Assert.Throws<UnauthorizedAccessException>(() => PathWhitelistGuard.ThrowIfForbidden(path));
    }

    [Theory]
    [InlineData(@"C:\synthetic\MOBAdev\manifest.md")]
    [InlineData(@"C:\synthetic\MOBAdev\agents\antigravity\agents\head-of-ai-strategy.md")]
    [InlineData(@"C:\synthetic\.codex\state_5.sqlite")]
    [InlineData(@"C:\synthetic\.codex\automations\td\automation.toml")]
    [InlineData(@"C:\synthetic\.claude\sessions\100.json")]
    public void IsAllowedFilePath_AllowsExpectedMetadataAndMarkdownFiles(string path)
    {
        Assert.True(PathWhitelistGuard.IsAllowedFilePath(path));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5179")]
    [InlineData("http://localhost:5179")]
    [InlineData("https://127.0.0.1:5180")]
    [InlineData("http://[::1]:5179")]
    public void LoopbackBindingGuard_AllowsLoopbackUrls(string url)
    {
        Assert.True(LoopbackBindingGuard.IsLoopbackUrl(url));
        Assert.Equal(url, LoopbackBindingGuard.EnsureLoopbackUrl(url));
    }

    [Theory]
    [InlineData("http://0.0.0.0:5179")]
    [InlineData("http://*:5179")]
    [InlineData("http://192.168.1.10:5179")]
    [InlineData("https://example.com:5179")]
    [InlineData("ftp://127.0.0.1:5179")]
    [InlineData("")]
    public void LoopbackBindingGuard_RejectsNonLoopbackOrInvalidUrls(string url)
    {
        Assert.False(LoopbackBindingGuard.IsLoopbackUrl(url));
        Assert.Throws<InvalidOperationException>(() => LoopbackBindingGuard.EnsureLoopbackUrl(url));
    }
}
