using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Gateway.Endpoints;

public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/schedule").WithTags("Schedule");

        group.MapPost("/parse", async (ScheduleParseRequest request,
            SmartScheduleParser parser,
            ILogger<GatewayProgramMarker> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.Input))
                return Results.BadRequest(new { error = "Input is required." });

            try
            {
                var parsed = await parser.ParseAsync(request.Input);

                if (parsed is null)
                    return Results.Json(
                        new { error = "Could not parse the schedule from the provided input. Try rephrasing." },
                        statusCode: StatusCodes.Status422UnprocessableEntity);

                return Results.Ok(parsed);
            }
            catch (ModelProviderUnavailableException ex)
            {
                logger.LogError(ex, "Model provider unavailable during schedule parse");
                return Results.Json(
                    new { error = "Model provider is unavailable. Cannot parse schedule." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "HTTP error during schedule parse");
                return Results.Json(
                    new { error = "Model provider is unavailable. Cannot parse schedule." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("ParseSchedule")
        .WithDescription("Parse a natural-language schedule into structured cron data");
    }
}

public sealed record ScheduleParseRequest
{
    public required string Input { get; init; }
}
