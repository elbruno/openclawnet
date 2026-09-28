using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Integrations.Jev;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Gateway;

public sealed class AgentProfileDecisionRouterTests
{
    [Fact]
    public async Task ResolveAsync_ShadowMode_KeepsDeterministicProfile()
    {
        var (router, decisions) = CreateRouter(shadow: true);

        var profile = await router.ResolveAsync("message", DefaultProfile(), false);

        profile.Name.Should().Be("default");
        decisions.Verify(
            x => x.DecideAsync(It.IsAny<AgentProfileDecisionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_ApplyMode_UsesAllowedRecommendation()
    {
        var (router, _) = CreateRouter(shadow: false);

        var profile = await router.ResolveAsync("message", DefaultProfile(), false);

        profile.Name.Should().Be("specialist");
    }

    [Fact]
    public async Task ResolveAsync_ExplicitProfile_SkipsDecision()
    {
        var (router, decisions) = CreateRouter(shadow: false);
        var explicitProfile = SpecialistProfile();

        var profile = await router.ResolveAsync("message", explicitProfile, true);

        profile.Should().BeSameAs(explicitProfile);
        decisions.Verify(
            x => x.DecideAsync(It.IsAny<AgentProfileDecisionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_FiltersDisabledAndNonStandardProfiles()
    {
        AgentProfileDecisionRequest? captured = null;
        var (router, decisions) = CreateRouter(shadow: true);
        decisions
            .Setup(x => x.DecideAsync(It.IsAny<AgentProfileDecisionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentProfileDecisionRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Recommendation());

        await router.ResolveAsync("message", DefaultProfile(), false);

        captured.Should().NotBeNull();
        captured!.Candidates.Select(x => x.Name).Should().BeEquivalentTo("default", "specialist");
        captured.Candidates.Select(x => x.Description)
            .Should().OnlyContain(description => !description.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsync_DecisionFailure_KeepsDeterministicProfile()
    {
        var (router, decisions) = CreateRouter(shadow: false);
        decisions
            .Setup(x => x.DecideAsync(It.IsAny<AgentProfileDecisionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("synthetic"));

        var profile = await router.ResolveAsync("message", DefaultProfile(), false);

        profile.Name.Should().Be("default");
    }

    [Fact]
    public async Task ResolveAsync_BoundsCandidatesAndKeepsDeterministicProfile()
    {
        AgentProfileDecisionRequest? captured = null;
        var profiles = Enumerable.Range(0, 40)
            .Select(index => new AgentProfile
            {
                Name = $"profile-{index:D2}",
                IsEnabled = true,
                Kind = ProfileKind.Standard
            })
            .ToList();
        var deterministic = new AgentProfile
        {
            Name = "zz-default",
            IsEnabled = true,
            Kind = ProfileKind.Standard
        };
        var store = new Mock<IAgentProfileStore>();
        store.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);
        var decisions = new Mock<IAgentProfileDecisionService>();
        decisions
            .Setup(x => x.DecideAsync(It.IsAny<AgentProfileDecisionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentProfileDecisionRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AgentProfileDecisionResult(
                AgentProfileDecisionStatus.InvalidResponse,
                null,
                null,
                TimeSpan.Zero));
        var router = new AgentProfileDecisionRouter(
            decisions.Object,
            store.Object,
            new JevRoutingOptions { Enabled = true, Shadow = true },
            NullLogger<AgentProfileDecisionRouter>.Instance);

        await router.ResolveAsync("message", deterministic, false);

        captured.Should().NotBeNull();
        captured!.Candidates.Should().HaveCount(32);
        captured.Candidates.Should().Contain(candidate => candidate.Name == deterministic.Name);
    }

    private static (AgentProfileDecisionRouter Router, Mock<IAgentProfileDecisionService> Decisions)
        CreateRouter(bool shadow)
    {
        var profiles = new[]
        {
            DefaultProfile(),
            SpecialistProfile(),
            new AgentProfile
            {
                Name = "disabled",
                DisplayName = "Disabled",
                IsEnabled = false,
                Kind = ProfileKind.Standard,
                Instructions = "secret disabled instructions"
            },
            new AgentProfile
            {
                Name = "system",
                DisplayName = "System",
                IsEnabled = true,
                Kind = ProfileKind.System,
                Instructions = "secret system instructions"
            }
        };
        var store = new Mock<IAgentProfileStore>();
        store.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);
        var decisions = new Mock<IAgentProfileDecisionService>();
        decisions
            .Setup(x => x.DecideAsync(It.IsAny<AgentProfileDecisionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Recommendation());
        var options = new JevRoutingOptions { Enabled = true, Shadow = shadow };

        return (
            new AgentProfileDecisionRouter(
                decisions.Object,
                store.Object,
                options,
                NullLogger<AgentProfileDecisionRouter>.Instance),
            decisions);
    }

    private static AgentProfileDecisionResult Recommendation() =>
        new(
            AgentProfileDecisionStatus.Recommended,
            "specialist",
            0.9,
            TimeSpan.FromMilliseconds(5));

    private static AgentProfile DefaultProfile() =>
        new()
        {
            Name = "default",
            DisplayName = "Default",
            Provider = "ollama",
            IsDefault = true,
            IsEnabled = true,
            Kind = ProfileKind.Standard,
            Instructions = "secret default instructions"
        };

    private static AgentProfile SpecialistProfile() =>
        new()
        {
            Name = "specialist",
            DisplayName = "Specialist",
            Provider = "azure-openai",
            Model = "specialist-model",
            IsEnabled = true,
            Kind = ProfileKind.Standard,
            Instructions = "secret specialist instructions"
        };
}
