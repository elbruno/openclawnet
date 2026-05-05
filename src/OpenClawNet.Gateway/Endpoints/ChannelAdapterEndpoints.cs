using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Gateway.Endpoints;

public static class ChannelAdapterEndpoints
{
    public static void MapChannelAdapterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/channel-adapters").WithTags("Channel Adapters");

        // GET /api/channel-adapters/{name} — adapter detail/config schema
        group.MapGet("/{name}", (
            string name,
            IChannelRegistry registry) =>
        {
            var adapter = registry.GetAllChannels()
                .FirstOrDefault(c => c.ChannelName.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (adapter is null)
                return Results.NotFound();

            return Results.Ok(new ChannelAdapterDetailDto(
                adapter.ChannelName,
                adapter.IsEnabled,
                adapter.GetType().FullName ?? adapter.GetType().Name,
                GetAdapterDescription(adapter.ChannelName)
            ));
        })
        .WithName("GetChannelAdapterDetail")
        .WithDescription("Get detail for a specific channel adapter");

        // GET /api/channel-adapters/{name}/health — is this adapter ready/configured?
        group.MapGet("/{name}/health", (
            string name,
            IChannelRegistry registry) =>
        {
            var adapter = registry.GetAllChannels()
                .FirstOrDefault(c => c.ChannelName.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (adapter is null)
                return Results.NotFound();

            var isHealthy = adapter.IsEnabled;
            var status = isHealthy ? "healthy" : "disabled";
            var message = isHealthy 
                ? $"Channel adapter '{name}' is enabled and ready" 
                : $"Channel adapter '{name}' is disabled";

            return Results.Ok(new ChannelAdapterHealthDto(
                name,
                status,
                isHealthy,
                message,
                DateTime.UtcNow
            ));
        })
        .WithName("GetChannelAdapterHealth")
        .WithDescription("Check if a channel adapter is ready and configured");
    }

    private static string GetAdapterDescription(string channelName) => channelName.ToLowerInvariant() switch
    {
        "teams" => "Microsoft Teams integration via Bot Framework",
        "slack" => "Slack integration via Bot Framework",
        "discord" => "Discord integration via Bot Framework",
        "telegram" => "Telegram integration via Bot Framework",
        _ => $"Channel adapter for {channelName}"
    };
}

public sealed record ChannelAdapterDetailDto(
    string Name,
    bool IsEnabled,
    string TypeName,
    string Description
);

public sealed record ChannelAdapterHealthDto(
    string Name,
    string Status,
    bool IsHealthy,
    string Message,
    DateTime CheckedAt
);
