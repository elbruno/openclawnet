using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using OpenClawNet.Tools.Abstractions;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Tool management endpoints — listing, manual invocation tests, and the LLM
/// "probe" mode that asks the configured ToolTester profile to translate a
/// natural-language prompt into JSON arguments and then runs the tool.
/// </summary>
public static class ToolTestEndpoints
{
    public static void MapToolTestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tools").WithTags("Tools");

        group.MapGet("/test-results", async (IToolTestRecordStore store, CancellationToken ct) =>
        {
            var records = await store.ListAsync(ct);
            return Results.Ok(records.Select(r => new ToolTestResultDto(
                r.Name, r.LastTestedAt, r.LastTestSucceeded, r.LastTestError, r.LastTestMode)));
        })
        .WithName("ListToolTestResults")
        .WithDescription("Returns the most recent test result for every tool that has ever been tested.");

        group.MapGet("/{name}/test-result", async (string name, IToolTestRecordStore store, CancellationToken ct) =>
        {
            var record = await store.GetAsync(name, ct);
            return record is null
                ? Results.Ok(new ToolTestResultDto(name, null, null, null, null))
                : Results.Ok(new ToolTestResultDto(record.Name, record.LastTestedAt,
                    record.LastTestSucceeded, record.LastTestError, record.LastTestMode));
        })
        .WithName("GetToolTestResult")
        .WithDescription("Returns the most recent test result for a single tool.");

        group.MapPost("/{name}/test", TestToolAsync)
            .WithName("TestTool")
            .WithDescription(
                "Runs a tool test. Modes: 'direct' (executes the tool with the provided JSON arguments — no LLM); " +
                "'probe' (uses the ToolTester profile to convert a natural-language prompt into JSON arguments, then runs the tool).");
    }

    internal static async Task<IResult> TestToolAsync(
        string name,
        [FromBody] ToolTestRequest request,
        IToolRegistry registry,
        IToolTestRecordStore recordStore,
        IAgentProfileStore profileStore,
        IModelProviderDefinitionStore providerStore,
        IEnumerable<IAgentProvider> providers,
        ILoggerFactory loggerFactory,
        IVaultSecretRedactor vaultRedactor,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TestTool");

        var tool = registry.GetTool(name);
        if (tool is null)
        {
            return Results.NotFound(new { error = $"Tool '{name}' not found." });
        }

        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "direct" : request.Mode.Trim().ToLowerInvariant();

        return mode switch
        {
            "direct" => await RunDirectAsync(tool, request, recordStore, logger, vaultRedactor, ct),
            "probe" => await RunProbeAsync(tool, request, recordStore, profileStore, providerStore, providers, logger, vaultRedactor, ct),
            _ => Results.BadRequest(new { error = $"Unknown mode '{mode}'. Use 'direct' or 'probe'." })
        };
    }

    // ── Direct Invoke ─────────────────────────────────────────────────────────

    private static async Task<IResult> RunDirectAsync(
        ITool tool,
        ToolTestRequest request,
        IToolTestRecordStore recordStore,
        ILogger logger,
        IVaultSecretRedactor vaultRedactor,
        CancellationToken ct)
    {
        var args = string.IsNullOrWhiteSpace(request.Arguments) ? "{}" : request.Arguments.Trim();

        // Validate JSON shape before invoking — surfaces a clear error rather than
        // letting the tool throw an opaque parser exception.
        try
        {
            using var _ = JsonDocument.Parse(args);
        }
        catch (JsonException ex)
        {
            await recordStore.SaveAsync(tool.Name, false, $"Invalid JSON arguments: {ex.Message}", "direct", ct);
            return Results.BadRequest(new ToolTestResponse(
                false, "Invalid JSON arguments: " + ex.Message, "direct", null, TimeSpan.Zero));
        }

        logger.LogInformation("Direct test of tool '{Tool}' with args: {Args}", tool.Name, vaultRedactor.Redact(args));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var input = new ToolInput { ToolName = tool.Name, RawArguments = args };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            var result = await tool.ExecuteAsync(input, cts.Token);
            sw.Stop();

            var summary = result.Success
                ? $"OK ({sw.ElapsedMilliseconds}ms): {Truncate(vaultRedactor.Redact(result.Output), 200)}"
                : $"FAIL: {vaultRedactor.Redact(result.Error)}";

            await recordStore.SaveAsync(tool.Name, result.Success, summary, "direct", ct);

            return Results.Ok(new ToolTestResponse(
                result.Success,
                summary,
                "direct",
                vaultRedactor.Redact(result.Output),
                sw.Elapsed));
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await recordStore.SaveAsync(tool.Name, false, "Test timed out (60s)", "direct", ct);
            return Results.Ok(new ToolTestResponse(false, "Test timed out (60s)", "direct", null, sw.Elapsed));
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Direct tool test '{Tool}' threw", tool.Name);
            var msg = $"Exception: {ex.Message}";
            await recordStore.SaveAsync(tool.Name, false, msg, "direct", ct);
            return Results.Ok(new ToolTestResponse(false, msg, "direct", null, sw.Elapsed));
        }
    }

    // ── Agent Probe ───────────────────────────────────────────────────────────

    private static async Task<IResult> RunProbeAsync(
        ITool tool,
        ToolTestRequest request,
        IToolTestRecordStore recordStore,
        IAgentProfileStore profileStore,
        IModelProviderDefinitionStore providerStore,
        IEnumerable<IAgentProvider> providers,
        ILogger logger,
        IVaultSecretRedactor vaultRedactor,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Results.BadRequest(new { error = "Probe mode requires a 'prompt' field with a natural-language request." });
        }

        var allProfiles = await profileStore.ListAsync(ct);

        // Resolve ToolTester profile: explicit name wins, otherwise pick first enabled ToolTester.
        AgentProfile? tester = null;
        if (!string.IsNullOrWhiteSpace(request.TesterProfileName))
        {
            tester = allProfiles.FirstOrDefault(p =>
                string.Equals(p.Name, request.TesterProfileName, StringComparison.OrdinalIgnoreCase));
        }
        tester ??= allProfiles.FirstOrDefault(p => p.Kind == ProfileKind.ToolTester && p.IsEnabled);

        if (tester is null)
        {
            const string err = "No ToolTester profile configured. Create one on the Agent Profiles page first.";
            await recordStore.SaveAsync(tool.Name, false, err, "probe", ct);
            return Results.Ok(new ToolTestResponse(false, err, "probe", null, TimeSpan.Zero));
        }

        if (tester.Kind != ProfileKind.ToolTester)
        {
            const string err = "The selected profile is not a ToolTester profile.";
            await recordStore.SaveAsync(tool.Name, false, err, "probe", ct);
            return Results.BadRequest(new ToolTestResponse(false, err, "probe", null, TimeSpan.Zero));
        }

        // Resolve provider definition → concrete chat client.
        ModelProviderDefinition? definition = null;
        if (!string.IsNullOrEmpty(tester.Provider))
        {
            definition = await providerStore.GetAsync(tester.Provider, ct);
        }
        if (definition is null)
        {
            var err = $"ToolTester profile '{tester.Name}' references missing provider '{tester.Provider}'.";
            await recordStore.SaveAsync(tool.Name, false, err, "probe", ct);
            return Results.Ok(new ToolTestResponse(false, err, "probe", null, TimeSpan.Zero));
        }

        var agentProvider = providers
            .Where(p => p.GetType().Name != "RuntimeAgentProvider")
            .FirstOrDefault(p => p.ProviderName.Equals(definition.ProviderType, StringComparison.OrdinalIgnoreCase));

        if (agentProvider is null)
        {
            var err = $"No registered IAgentProvider for type '{definition.ProviderType}'.";
            await recordStore.SaveAsync(tool.Name, false, err, "probe", ct);
            return Results.Ok(new ToolTestResponse(false, err, "probe", null, TimeSpan.Zero));
        }

        var chatProfile = new AgentProfile
        {
            Name = $"probe-{tester.Name}",
            Provider = definition.ProviderType,
            Endpoint = definition.Endpoint,
            ApiKey = definition.ApiKey,
            DeploymentName = definition.DeploymentName,
            AuthMode = definition.AuthMode,
            Instructions = tester.Instructions,
        };

        IChatClient chatClient;
        try
        {
            chatClient = agentProvider.CreateChatClient(chatProfile);
        }
        catch (Exception ex)
        {
            var err = $"Failed to construct chat client: {ex.Message}";
            await recordStore.SaveAsync(tool.Name, false, err, "probe", ct);
            return Results.Ok(new ToolTestResponse(false, err, "probe", null, TimeSpan.Zero));
        }

        var schemaJson = tool.Metadata.ParameterSchema.RootElement.GetRawText();
        var systemPrompt =
            "You convert natural-language requests into a JSON arguments object for a single tool.\n" +
            "Return ONLY a JSON object that conforms to the schema below. No markdown, no explanation.\n" +
            "Tool name: " + tool.Name + "\n" +
            "Tool description: " + tool.Description + "\n" +
            "Argument schema (JSON Schema):\n" + schemaJson;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            var response = await chatClient.GetResponseAsync(
                [
                    new AIChatMessage(ChatRole.System, systemPrompt),
                    new AIChatMessage(ChatRole.User, request.Prompt!),
                ],
                cancellationToken: cts.Token);

            var raw = response.Text ?? string.Empty;
            var args = ExtractJson(raw);
            if (args is null)
            {
                sw.Stop();
                var err = "ToolTester model did not return a JSON object. Raw response: " + Truncate(raw, 200);
                await recordStore.SaveAsync(tool.Name, false, err, "probe", ct);
                return Results.Ok(new ToolTestResponse(false, err, "probe", raw, sw.Elapsed));
            }

            // Now invoke the tool with the model-suggested args.
            var input = new ToolInput { ToolName = tool.Name, RawArguments = args };
            var result = await tool.ExecuteAsync(input, cts.Token);
            sw.Stop();

            var summary = result.Success
                ? $"Probe OK ({sw.ElapsedMilliseconds}ms). Args: {Truncate(vaultRedactor.Redact(args), 80)}"
                : $"Probe FAIL: {vaultRedactor.Redact(result.Error)} (Args: {Truncate(vaultRedactor.Redact(args), 80)})";

            await recordStore.SaveAsync(tool.Name, result.Success, summary, "probe", ct);
            return Results.Ok(new ToolTestResponse(
                result.Success, summary, "probe", vaultRedactor.Redact(result.Output), sw.Elapsed));
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await recordStore.SaveAsync(tool.Name, false, "Probe timed out (45s)", "probe", ct);
            return Results.Ok(new ToolTestResponse(false, "Probe timed out (45s)", "probe", null, sw.Elapsed));
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Probe tool test '{Tool}' threw", tool.Name);
            var msg = $"Probe exception: {ex.Message}";
            await recordStore.SaveAsync(tool.Name, false, msg, "probe", ct);
            return Results.Ok(new ToolTestResponse(false, msg, "probe", null, sw.Elapsed));
        }
    }

    private static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var first = raw.IndexOf('{');
        var last = raw.LastIndexOf('}');
        if (first < 0 || last <= first) return null;
        var candidate = raw[first..(last + 1)];
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return candidate;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");
}

public sealed record ToolTestRequest
{
    /// <summary><c>direct</c> (no LLM, runs with provided JSON args) or <c>probe</c> (LLM converts prompt → args).</summary>
    public string? Mode { get; init; } = "direct";

    /// <summary>JSON object with the tool's arguments. Used in <c>direct</c> mode.</summary>
    public string? Arguments { get; init; }

    /// <summary>Natural-language request. Used in <c>probe</c> mode to ask the LLM to build arguments.</summary>
    public string? Prompt { get; init; }

    /// <summary>Optional explicit ToolTester profile name. If null, the first enabled ToolTester is used.</summary>
    public string? TesterProfileName { get; init; }
}

public sealed record ToolTestResponse(
    bool Success,
    string Message,
    string Mode,
    string? Output,
    TimeSpan Duration);

public sealed record ToolTestResultDto(
    string Name,
    DateTime? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestError,
    string? LastTestMode);
