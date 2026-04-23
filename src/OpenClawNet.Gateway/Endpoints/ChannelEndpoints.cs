using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// API endpoints for querying registered communication channels.
/// </summary>
public static class ChannelEndpoints
{
    /// <summary>
    /// Maps channel-related API endpoints onto the application's route table.
    /// </summary>
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder app)
    {
        // NOTE: Route is /api/channel-adapters (not /api/channels) to avoid collision
        // with the Phase 1 Job Output Dashboard endpoints in ChannelsApiEndpoints, which
        // own /api/channels for job artifact channels.
        app.MapGet("/api/channel-adapters", (IChannelRegistry registry) =>
        {
            var channels = registry.GetAllChannels()
                .Select(c => new { name = c.ChannelName, enabled = c.IsEnabled });

            return Results.Ok(channels);
        })
        .WithTags("ChannelAdapters")
        .WithName("ListChannelAdapters")
        .WithDescription("Returns all registered delivery channel adapters (Teams, Slack, etc.) and their enabled state.");

        return app;
    }
}
