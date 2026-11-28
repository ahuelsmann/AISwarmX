using System.Text;
using AISwarmX.Dashboard.Api.Providers;
using Xunit;

namespace AISwarmX.Dashboard.Api.Tests;

public sealed class ProcessCommandRunnerTests
{
    [Fact]
    public void CreateStartInfo_ConfiguresUtf8StandardOutputAndErrorEncodings()
    {
        var startInfo = ProcessCommandRunner.CreateStartInfo(
            "gh",
            ["issue", "list", "--json", "title"],
            workingDirectory: null);

        Assert.Equal(Encoding.UTF8, startInfo.StandardOutputEncoding);
        Assert.Equal(Encoding.UTF8, startInfo.StandardErrorEncoding);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["issue", "list", "--json", "title"], startInfo.ArgumentList);
    }
}
