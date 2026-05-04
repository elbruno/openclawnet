namespace OpenClawNet.Channels.Adapters;

/// <summary>
/// Factory for creating channel delivery adapters by type.
/// </summary>
public interface IChannelDeliveryAdapterFactory
{
    /// <summary>
    /// Create an adapter instance for the specified adapter type.
    /// </summary>
    /// <param name="adapterType">The adapter type (e.g., "GenericWebhook", "Teams", "Slack")</param>
    /// <returns>An adapter instance</returns>
    /// <exception cref="InvalidOperationException">Thrown if the adapter type is unknown</exception>
    IChannelDeliveryAdapter CreateAdapter(string adapterType);
}
