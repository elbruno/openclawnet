using Bunit;
using FluentAssertions;
using OpenClawNet.Web.Components.Layout;

namespace OpenClawNet.UnitTests.Web.Layout;

public class ReconnectModalTests : TestContext, IDisposable
{
    [Fact]
    public void RendersLocalRecoveryGuidance()
    {
        var cut = Render<ReconnectModal>();

        cut.Markup.Should().Contain("Rejoining OpenClaw .NET services...");
        cut.Markup.Should().Contain("If you're running locally, make sure the services are up with");
        cut.FindAll("code")
            .Select(element => element.TextContent.Trim())
            .Should()
            .Contain("aspire start");
    }

    [Fact]
    public void FailedStateAction_UsesRetryNowLabel()
    {
        var cut = Render<ReconnectModal>();

        cut.Find("#components-reconnect-button").TextContent.Trim().Should().Be("Retry now");
    }
}
