using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Channels.Adapters;

namespace OpenClawNet.UnitTests.Channels;

public sealed class ChannelDeliveryAdapterFactoryTests
{
    [Fact]
    public void CreateAdapter_WithGenericWebhook_ReturnsGenericWebhookAdapter()
    {
        var services = new ServiceCollection();
        services.AddHttpClient<GenericWebhookAdapter>();
        services.AddScoped<GenericWebhookAdapter>();
        services.AddScoped<TeamsProactiveAdapter>();
        services.AddScoped<SlackWebhookAdapter>();
        services.AddScoped<IChannelDeliveryAdapterFactory, ChannelDeliveryAdapterFactory>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        var adapter = factory.CreateAdapter("GenericWebhook");

        adapter.Should().BeOfType<GenericWebhookAdapter>();
        adapter.Name.Should().Be("GenericWebhook");
    }

    [Fact]
    public void CreateAdapter_WithTeams_ReturnsTeamsProactiveAdapter()
    {
        var services = new ServiceCollection();
        services.AddHttpClient<TeamsProactiveAdapter>();
        services.AddScoped<GenericWebhookAdapter>();
        services.AddScoped<TeamsProactiveAdapter>();
        services.AddScoped<SlackWebhookAdapter>();
        services.AddScoped<IChannelDeliveryAdapterFactory, ChannelDeliveryAdapterFactory>();

        // TeamsProactiveAdapter needs IConfiguration
        var configDict = new Dictionary<string, string?>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build());

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        var adapter = factory.CreateAdapter("Teams");

        adapter.Should().BeOfType<TeamsProactiveAdapter>();
        adapter.Name.Should().Be("Teams");
    }

    [Fact]
    public void CreateAdapter_WithSlack_ReturnsSlackWebhookAdapter()
    {
        var services = new ServiceCollection();
        services.AddHttpClient<SlackWebhookAdapter>();
        services.AddScoped<GenericWebhookAdapter>();
        services.AddScoped<TeamsProactiveAdapter>();
        services.AddScoped<SlackWebhookAdapter>();
        services.AddScoped<IChannelDeliveryAdapterFactory, ChannelDeliveryAdapterFactory>();
        
        // SlackWebhookAdapter needs IConfiguration
        var configDict = new Dictionary<string, string?>();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build());

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        var adapter = factory.CreateAdapter("Slack");

        adapter.Should().BeOfType<SlackWebhookAdapter>();
        adapter.Name.Should().Be("Slack");
    }

    [Fact]
    public void CreateAdapter_WithUnknownType_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<GenericWebhookAdapter>();
        services.AddScoped<TeamsProactiveAdapter>();
        services.AddScoped<SlackWebhookAdapter>();
        services.AddScoped<IChannelDeliveryAdapterFactory, ChannelDeliveryAdapterFactory>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        var action = () => factory.CreateAdapter("UnknownAdapter");

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Unknown adapter type: UnknownAdapter");
    }

    [Fact]
    public void CreateAdapter_WithNullType_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        services.AddScoped<GenericWebhookAdapter>();
        services.AddScoped<TeamsProactiveAdapter>();
        services.AddScoped<SlackWebhookAdapter>();
        services.AddScoped<IChannelDeliveryAdapterFactory, ChannelDeliveryAdapterFactory>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IChannelDeliveryAdapterFactory>();

        var action = () => factory.CreateAdapter(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}
