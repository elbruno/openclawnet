using ElBruno.AI.Jev;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenClawNet.Integrations.Jev;

namespace OpenClawNet.UnitTests.Jev;

public sealed class JevAgentProfileDecisionServiceTests
{
    [Fact]
    public async Task DecideAsync_ValidChoice_ReturnsRecommendation()
    {
        var client = new Mock<IJevDecisionClient>();
        client
            .Setup(x => x.EvaluateAsync(It.IsAny<JevDecisionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("specialist"));
        var service = CreateService(client.Object);

        var result = await service.DecideAsync(Request());

        result.Status.Should().Be(AgentProfileDecisionStatus.Recommended);
        result.RecommendedProfile.Should().Be("specialist");
        result.Confidence.Should().Be(0.9);
    }

    [Fact]
    public async Task DecideAsync_UnknownChoice_ReturnsInvalidResponse()
    {
        var client = new Mock<IJevDecisionClient>();
        client
            .Setup(x => x.EvaluateAsync(It.IsAny<JevDecisionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("unknown"));
        var service = CreateService(client.Object);

        var result = await service.DecideAsync(Request());

        result.Status.Should().Be(AgentProfileDecisionStatus.InvalidResponse);
        result.FailureCode.Should().Be("unknown_profile");
    }

    [Fact]
    public async Task DecideAsync_InternalTimeout_ReturnsFallback()
    {
        var client = new Mock<IJevDecisionClient>();
        client
            .Setup(x => x.EvaluateAsync(It.IsAny<JevDecisionRequest>(), It.IsAny<CancellationToken>()))
            .Returns<JevDecisionRequest, CancellationToken>(
                async (_, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Response("default");
                });
        var service = CreateService(client.Object, TimeSpan.FromMilliseconds(20));

        var result = await service.DecideAsync(Request());

        result.Status.Should().Be(AgentProfileDecisionStatus.TimedOut);
        result.FailureCode.Should().Be("timeout");
    }

    [Fact]
    public async Task DecideAsync_CallerCancellation_Propagates()
    {
        var client = new Mock<IJevDecisionClient>();
        client
            .Setup(x => x.EvaluateAsync(It.IsAny<JevDecisionRequest>(), It.IsAny<CancellationToken>()))
            .Returns<JevDecisionRequest, CancellationToken>(
                async (_, token) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return Response("default");
                });
        var service = CreateService(client.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => service.DecideAsync(Request(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DecideAsync_ClientFailure_ReturnsFallback()
    {
        var client = new Mock<IJevDecisionClient>();
        client
            .Setup(x => x.EvaluateAsync(It.IsAny<JevDecisionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("synthetic failure"));
        var service = CreateService(client.Object);

        var result = await service.DecideAsync(Request());

        result.Status.Should().Be(AgentProfileDecisionStatus.Failed);
        result.FailureCode.Should().Be(nameof(HttpRequestException));
    }

    [Fact]
    public async Task Registration_EnabledWithoutApiKey_ReturnsMisconfiguredFallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptionalJevAgentProfileDecisions(new JevRoutingOptions
        {
            Enabled = true,
            ApiKey = string.Empty
        });
        using var provider = services.BuildServiceProvider();

        var result = await provider
            .GetRequiredService<IAgentProfileDecisionService>()
            .DecideAsync(Request());

        result.Status.Should().Be(AgentProfileDecisionStatus.Misconfigured);
        result.FailureCode.Should().Be("missing_api_key");
    }

    private static IAgentProfileDecisionService CreateService(
        IJevDecisionClient client,
        TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(client);
        services.AddOptionalJevAgentProfileDecisions(new JevRoutingOptions
        {
            Enabled = true,
            ApiKey = "offline-test-key",
            Timeout = timeout ?? TimeSpan.FromSeconds(2)
        });
        return services.BuildServiceProvider().GetRequiredService<IAgentProfileDecisionService>();
    }

    private static AgentProfileDecisionRequest Request() =>
        new(
            "Route this request",
            [
                new("default", "Default profile"),
                new("specialist", "Specialist profile")
            ]);

    private static JevDecisionResponse Response(string choice)
    {
        var probabilities = new Dictionary<string, double>
        {
            ["default"] = choice == "default" ? 0.9 : 0.1,
            ["specialist"] = choice == "specialist" ? 0.9 : 0.05
        };
        if (!probabilities.ContainsKey(choice))
        {
            probabilities[choice] = 0.9;
        }

        return new(
            "offline-test-model",
            new Dictionary<string, JevAnswer>
            {
                ["agent_profile"] = new JevChoiceAnswer(
                    choice,
                    probabilities,
                    0.9)
            });
    }
}
