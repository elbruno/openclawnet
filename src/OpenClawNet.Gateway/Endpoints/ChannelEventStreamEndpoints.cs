using System.Text.Json;
using OpenClawNet.Gateway.Services;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Concept-review §5 (UX) — NDJSON streaming endpoint that mirrors the chat
/// pattern (<c>/api/chat/stream</c>). The /channels page subscribes here when
/// it wants real-time updates and falls back to plain polling otherwise.
/// </summary>
public static class ChannelEventStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapChannelEventStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/api/channels/{jobId:guid}/stream", async (
            Guid jobId,
            IChannelEventBus bus,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.ContentType = "application/x-ndjson";
            httpContext.Response.Headers["Cache-Control"] = "no-cache";

            // Initial heartbeat so the client knows the stream is live.
            await httpContext.Response.WriteAsync(
                JsonSerializer.Serialize(new ChannelEvent("hello", jobId, null, null, DateTime.UtcNow), JsonOpts) + "\n",
                cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);

            await foreach (var evt in bus.Subscribe(jobId, cancellationToken))
            {
                await httpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(evt, JsonOpts) + "\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
        })
        .WithName("StreamChannelEvents")
        .WithTags("Channels")
        .WithDescription("NDJSON stream of channel events (artifact created, etc.) for one job.");
    }
}
