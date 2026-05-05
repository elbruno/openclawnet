using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using OpenClawNet.Web.Components.Skills;
using OpenClawNet.Web.Models.Skills;
using OpenClawNet.Web.Services;

namespace OpenClawNet.UnitTests.Web.Skills;

public class SkillAuthoringDialogTests : TestContext, IDisposable
{
    private readonly Mock<HttpMessageHandler> _handler = new();

    public SkillAuthoringDialogTests()
    {
        var http = new HttpClient(_handler.Object) { BaseAddress = new Uri("http://gateway/") };
        Services.AddSingleton(new SkillsClient(http));
    }

    [Fact]
    public void Submit_Disabled_WhenAllFieldsEmpty()
    {
        var cut = RenderComponent<SkillAuthoringDialog>(p => p.Add(x => x.Visible, true));
        cut.Find("[data-testid='skill-submit']").HasAttribute("disabled").Should().BeTrue();
    }

    [Theory]
    [InlineData("-leading-hyphen")]
    [InlineData(".leading-dot")]
    [InlineData("has space")]
    [InlineData("memory")]
    [InlineData("doc-processor")]
    [InlineData("MEMORY")]
    public void Submit_StaysDisabled_OnInvalidOrReservedName(string name)
    {
        var cut = RenderComponent<SkillAuthoringDialog>(p => p.Add(x => x.Visible, true));
        cut.Find("[data-testid='skill-name']").Input(name);
        cut.Find("[data-testid='skill-description']").Input("desc");
        cut.Find("[data-testid='skill-body']").Input("# body");
        cut.Find("[data-testid='skill-submit']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Submit_Enables_WhenNameValidAndDescriptionAndBodyPresent()
    {
        var cut = RenderComponent<SkillAuthoringDialog>(p => p.Add(x => x.Visible, true));
        cut.Find("[data-testid='skill-name']").Input("git-tutor");
        cut.Find("[data-testid='skill-description']").Input("help juniors learn git");
        cut.Find("[data-testid='skill-body']").Input("# Git Tutor\nbody");
        cut.Find("[data-testid='skill-submit']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Submit_OnServer400_RendersAlertWithReason()
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new SkillsProblem("InvalidSkillName", "name not allowed"))
            });

        var cut = RenderComponent<SkillAuthoringDialog>(p => p.Add(x => x.Visible, true));
        cut.Find("[data-testid='skill-name']").Input("git-tutor");
        cut.Find("[data-testid='skill-description']").Input("desc");
        cut.Find("[data-testid='skill-body']").Input("# body");
        cut.Find("[data-testid='skill-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='server-error']").TextContent.Should().Contain("InvalidSkillName");
        }, timeout: TimeSpan.FromSeconds(3));
    }
}
