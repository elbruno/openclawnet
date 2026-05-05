using FluentAssertions;
using OpenClawNet.Mcp.Abstractions;
using Xunit;

namespace OpenClawNet.UnitTests.Mcp;

public sealed class McpProcessIsolationPolicyTests
{
    [Fact]
    public void NoIsolationPolicy_LeavesPlanUntouched()
    {
        var plan = new McpProcessLaunchPlan
        {
            ServerName = "demo",
            Executable = "node",
            Arguments = new List<string> { "server.js" },
            Environment = new Dictionary<string, string?> { ["SECRET"] = "shh", ["PATH"] = "/bin" },
            WorkingDirectory = "/some/place",
        };

        new NoIsolationPolicy().Apply(plan);

        plan.Executable.Should().Be("node");
        plan.WorkingDirectory.Should().Be("/some/place");
        plan.Environment.Should().ContainKey("SECRET");
    }

    [Fact]
    public void WorkingDirIsolationPolicy_ScrubsEnvAndCreatesTempDir()
    {
        var plan = new McpProcessLaunchPlan
        {
            ServerName = "demo-server",
            Executable = "node",
            Arguments = new List<string> { "server.js" },
            Environment = new Dictionary<string, string?>
            {
                ["SECRET"] = "shh",
                ["PATH"] = "/usr/bin",
                ["AZURE_OPENAI_API_KEY"] = "leak",
            },
        };

        new WorkingDirIsolationPolicy().Apply(plan);

        plan.WorkingDirectory.Should().NotBeNullOrEmpty();
        Directory.Exists(plan.WorkingDirectory!).Should().BeTrue();
        plan.Environment.Should().NotContainKey("SECRET");
        plan.Environment.Should().NotContainKey("AZURE_OPENAI_API_KEY");
        plan.Environment.Should().ContainKey("PATH");
        plan.Environment["PATH"].Should().Be("/usr/bin");
    }

    [Fact]
    public void WorkingDirIsolationPolicy_HandlesMissingPath_StillScrubs()
    {
        var plan = new McpProcessLaunchPlan
        {
            ServerName = "no-path-server",
            Executable = "node",
            Arguments = new List<string>(),
            Environment = new Dictionary<string, string?> { ["LEAK"] = "x" },
        };

        new WorkingDirIsolationPolicy().Apply(plan);

        plan.Environment.Should().BeEmpty();
        plan.WorkingDirectory.Should().NotBeNull();
    }

    [Fact]
    public void WorkingDirIsolationPolicy_SanitizesServerNameForFilesystem()
    {
        var plan = new McpProcessLaunchPlan
        {
            ServerName = "weird:name/with*chars",
            Executable = "node",
            Arguments = new List<string>(),
            Environment = new Dictionary<string, string?>(),
        };

        var act = () => new WorkingDirIsolationPolicy().Apply(plan);

        act.Should().NotThrow();
        plan.WorkingDirectory.Should().NotBeNull();
    }
}
