namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Hardcoded adapter factory using dependency injection to resolve adapter instances.
/// </summary>
public class ChannelDeliveryAdapterFactory : IChannelDeliveryAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ChannelDeliveryAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IChannelDeliveryAdapter CreateAdapter(string adapterType)
    {
        ArgumentNullException.ThrowIfNull(adapterType);

        return adapterType switch
        {
            "GenericWebhook" => _serviceProvider.GetRequiredService<GenericWebhookAdapter>(),
            "Teams" => _serviceProvider.GetRequiredService<TeamsProactiveAdapter>(),
            "Slack" => _serviceProvider.GetRequiredService<SlackWebhookAdapter>(),
            "SlackProactive" => _serviceProvider.GetRequiredService<SlackProactiveAdapter>(),
            _ => throw new InvalidOperationException($"Unknown adapter type: {adapterType}")
        };
    }
}
