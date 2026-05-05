using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using OpenClawNet.Web.Components.Skills;
using OpenClawNet.Web.Models.Skills;
using OpenClawNet.Web.Services;

namespace OpenClawNet.UnitTests.Web.Skills;

public class SkillEnableMatrixTests : TestContext, IDisposable
{
    private readonly Mock<HttpMessageHandler> _handler = new();

    public SkillEnableMatrixTests()
    {
        var http = new HttpClient(_handler.Object) { BaseAddress = new Uri("http://gateway/") };
        Services.AddSingleton(new SkillsClient(http));
    }

    private static SkillDto MakeSkill(params (string agent, bool enabled)[] state) =>
        new("git-tutor", "test", "1.0", "installed", null, "manual", null, "sha256:abc",
            DateTimeOffset.UtcNow, "installed", state.ToDictionary(t => t.agent, t => t.enabled));

    [Fact]
    public void RendersOneToggle_PerAgent()
    {
        var skill = MakeSkill(("chat-bot", true), ("analyst", false));
        var cut = RenderComponent<SkillEnableMatrix>(p => p
            .Add(x => x.Skill, skill)
            .Add(x => x.Agents, new[] { "chat-bot", "analyst" }));

        cut.FindAll("[data-testid^='toggle-']").Should().HaveCount(2);
    }

    [Fact]
    public async Task TogglingAgent_SendsPutToCorrectEndpoint_AfterDebounce()
    {
        HttpRequestMessage? captured = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var skill = MakeSkill(("chat-bot", false));
        var cut = RenderComponent<SkillEnableMatrix>(p => p
            .Add(x => x.Skill, skill)
            .Add(x => x.Agents, new[] { "chat-bot" }));

        cut.Find("[data-testid='toggle-chat-bot']").Change(true);

        // Spec §4.1: ~1s debounce. Wait it out + a small slack.
        await Task.Delay(1500);
        cut.WaitForAssertion(() => captured.Should().NotBeNull(), timeout: TimeSpan.FromSeconds(3));

        captured!.Method.Should().Be(HttpMethod.Put);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/skills/git-tutor/enabled-for/chat-bot");
        var body = await captured.Content!.ReadAsStringAsync();
        body.Should().Contain("\"enabled\":true");
    }
}
