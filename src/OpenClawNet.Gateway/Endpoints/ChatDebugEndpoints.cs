using System.Diagnostics;
using System.Text;
using OpenClawNet.Gateway.Services;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Diagnostic endpoints that bypass the full agent pipeline to isolate chat hangs.
/// Tests the model client directly — if this works, the issue is in the orchestrator/runtime.
/// </summary>
public static class ChatDebugEndpoints
{
    public static void MapChatDebugEndpoints(this WebApplication app)
    {
        // Minimal DI resolution test — does resolving the orchestrator hang?
        app.MapGet("/api/chat/debug/ping", (
            OpenClawNet.Agent.IAgentOrchestrator orchestrator,
            ILogger<GatewayProgramMarker> logger) =>
        {
            logger.LogInformation("[DIAG-PING] Orchestrator resolved: {Type}", orchestrator.GetType().Name);
            return Results.Ok(new { ok = true, orchestratorType = orchestrator.GetType().Name });
        })
        .WithName("ChatDebugPing")
        .WithTags("Debug");

        // Minimal step-by-step test — each step has its own timeout
        app.MapPost("/api/chat/debug/steps", async (
            OpenClawNet.Agent.IAgentOrchestrator orchestrator,
            OpenClawNet.Storage.IConversationStore conversationStore,
            OpenClawNet.Agent.ISummaryService summaryService,
            OpenClawNet.Agent.IPromptComposer promptComposer,
            IModelClient modelClient,
            ILogger<GatewayProgramMarker> logger,
            CancellationToken ct) =>
        {
            var sw = Stopwatch.StartNew();
            var results = new List<object>();
            var sessionId = Guid.NewGuid();

            logger.LogInformation("[DIAG-STEPS] Starting step-by-step test...");

            // Step 1: AddMessage (5s timeout)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var t0 = sw.ElapsedMilliseconds;
                await conversationStore.AddMessageAsync(sessionId, "user", "test", cancellationToken: linked.Token);
                results.Add(new { step = 1, name = "AddMessage", ok = true, ms = sw.ElapsedMilliseconds - t0 });
            }
            catch (Exception ex)
            {
                results.Add(new { step = 1, name = "AddMessage", ok = false, ms = sw.ElapsedMilliseconds, error = ex.GetType().Name + ": " + ex.Message });
                return Results.Ok(new { results, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 2: GetMessages (5s timeout)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var t0 = sw.ElapsedMilliseconds;
                var msgs = await conversationStore.GetMessagesAsync(sessionId, linked.Token);
                results.Add(new { step = 2, name = "GetMessages", ok = true, ms = sw.ElapsedMilliseconds - t0, count = msgs.Count });
            }
            catch (Exception ex)
            {
                results.Add(new { step = 2, name = "GetMessages", ok = false, ms = sw.ElapsedMilliseconds, error = ex.GetType().Name + ": " + ex.Message });
                return Results.Ok(new { results, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 3: SummarizeIfNeeded (5s timeout)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var t0 = sw.ElapsedMilliseconds;
                var history = new List<OpenClawNet.Models.Abstractions.ChatMessage>
                {
                    new() { Role = ChatMessageRole.User, Content = "test" }
                };
                var summary = await summaryService.SummarizeIfNeededAsync(sessionId, history, linked.Token);
                results.Add(new { step = 3, name = "SummarizeIfNeeded", ok = true, ms = sw.ElapsedMilliseconds - t0 });
            }
            catch (Exception ex)
            {
                results.Add(new { step = 3, name = "SummarizeIfNeeded", ok = false, ms = sw.ElapsedMilliseconds, error = ex.GetType().Name + ": " + ex.Message });
                return Results.Ok(new { results, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 4: Compose (5s timeout)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var t0 = sw.ElapsedMilliseconds;
                var ctx = new OpenClawNet.Agent.PromptContext
                {
                    SessionId = sessionId,
                    UserMessage = "test",
                    History = [],
                    SessionSummary = null
                };
                var msgs = await promptComposer.ComposeAsync(ctx, linked.Token);
                results.Add(new { step = 4, name = "Compose", ok = true, ms = sw.ElapsedMilliseconds - t0, count = msgs.Count });
            }
            catch (Exception ex)
            {
                results.Add(new { step = 4, name = "Compose", ok = false, ms = sw.ElapsedMilliseconds, error = ex.GetType().Name + ": " + ex.Message });
                return Results.Ok(new { results, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 5: Direct model stream (no tools, 30s timeout)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var t0 = sw.ElapsedMilliseconds;
                var request = new ChatRequest
                {
                    Messages = [new OpenClawNet.Models.Abstractions.ChatMessage { Role = ChatMessageRole.User, Content = "hi" }]
                };
                var chunks = 0;
                await foreach (var chunk in modelClient.StreamAsync(request, linked.Token))
                    chunks++;
                results.Add(new { step = 5, name = "ModelStream_NoTools", ok = true, ms = sw.ElapsedMilliseconds - t0, chunks });
            }
            catch (Exception ex)
            {
                results.Add(new { step = 5, name = "ModelStream_NoTools", ok = false, ms = sw.ElapsedMilliseconds, error = ex.GetType().Name + ": " + ex.Message });
                return Results.Ok(new { results, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 6: Orchestrator stream (30s timeout)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
                var t0 = sw.ElapsedMilliseconds;
                var req = new OpenClawNet.Agent.AgentRequest { SessionId = Guid.NewGuid(), UserMessage = "hi" };
                var events = new List<string>();
                await foreach (var evt in orchestrator.StreamAsync(req, linked.Token))
                {
                    events.Add($"{evt.Type}");
                    if (events.Count > 20) break;
                }
                results.Add(new { step = 6, name = "OrchestratorStream", ok = true, ms = sw.ElapsedMilliseconds - t0, events = string.Join(",", events) });
            }
            catch (OperationCanceledException)
            {
                results.Add(new { step = 6, name = "OrchestratorStream", ok = false, ms = sw.ElapsedMilliseconds, error = "TIMEOUT_30s" });
            }
            catch (Exception ex)
            {
                results.Add(new { step = 6, name = "OrchestratorStream", ok = false, ms = sw.ElapsedMilliseconds, error = ex.GetType().Name + ": " + ex.Message });
            }

            try { await conversationStore.DeleteSessionAsync(sessionId); } catch { }
            return Results.Ok(new { results, totalMs = sw.ElapsedMilliseconds });
        })
        .WithName("ChatDebugSteps")
        .WithTags("Debug");

        app.MapPost("/api/chat/debug", async (IModelClient modelClient, RuntimeModelSettings settings) =>
        {
            var sw = Stopwatch.StartNew();
            var cfg = settings.Current;

            var diagnostics = new
            {
                provider = cfg.Provider,
                endpoint = cfg.Endpoint,
                deployment = cfg.DeploymentName,
                model = cfg.Model,
                authMode = cfg.AuthMode,
                hasApiKey = !string.IsNullOrEmpty(cfg.ApiKey),
                clientType = modelClient.GetType().Name,
                providerName = modelClient.ProviderName,
                fallbacks = cfg.Fallbacks
            };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var request = new ChatRequest
                {
                    Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "Say hello in one sentence." }]
                };

                var response = await modelClient.CompleteAsync(request, cts.Token);
                sw.Stop();

                return Results.Ok(new
                {
                    status = "ok",
                    diagnostics,
                    response = new
                    {
                        content = response.Content,
                        model = response.Model,
                        finishReason = response.FinishReason,
                        usage = response.Usage
                    },
                    elapsedSeconds = sw.Elapsed.TotalSeconds
                });
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return Results.Json(new
                {
                    status = "timeout",
                    diagnostics,
                    error = "Model call timed out after 30 seconds — the model provider is not responding.",
                    elapsedSeconds = sw.Elapsed.TotalSeconds
                }, statusCode: 504);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Results.Json(new
                {
                    status = "error",
                    diagnostics,
                    error = ex.Message,
                    exceptionType = ex.GetType().FullName,
                    innerError = ex.InnerException?.Message,
                    elapsedSeconds = sw.Elapsed.TotalSeconds
                }, statusCode: 500);
            }
        })
        .WithName("ChatDebug")
        .WithTags("Debug")
        .WithDescription("Direct model test — bypasses the full agent pipeline to isolate hangs");

        app.MapPost("/api/chat/debug/stream", async (IModelClient modelClient, RuntimeModelSettings settings, HttpContext httpContext) =>
        {
            var sw = Stopwatch.StartNew();
            var cfg = settings.Current;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var request = new ChatRequest
                {
                    Messages = [new ChatMessage { Role = ChatMessageRole.User, Content = "Say hello in one sentence." }]
                };

                var chunks = new StringBuilder();
                var chunkCount = 0;

                await foreach (var chunk in modelClient.StreamAsync(request, cts.Token))
                {
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        chunks.Append(chunk.Content);
                        chunkCount++;
                    }
                }

                sw.Stop();
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    status = "ok",
                    provider = cfg.Provider,
                    providerName = modelClient.ProviderName,
                    content = chunks.ToString(),
                    chunkCount,
                    elapsedSeconds = sw.Elapsed.TotalSeconds
                });
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                httpContext.Response.StatusCode = 504;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    status = "timeout",
                    provider = cfg.Provider,
                    error = "Stream timed out after 30 seconds",
                    elapsedSeconds = sw.Elapsed.TotalSeconds
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                httpContext.Response.StatusCode = 500;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    status = "error",
                    provider = cfg.Provider,
                    error = ex.Message,
                    exceptionType = ex.GetType().FullName,
                    innerError = ex.InnerException?.Message,
                    elapsedSeconds = sw.Elapsed.TotalSeconds
                });
            }
        })
        .WithName("ChatDebugStream")
        .WithTags("Debug")
        .WithDescription("Direct streaming model test — bypasses the full agent pipeline");

        // Pipeline diagnostic — tests each step of the orchestrator pipeline independently
        app.MapPost("/api/chat/debug/pipeline", async (
            IModelClient modelClient,
            OpenClawNet.Agent.IAgentOrchestrator orchestrator,
            OpenClawNet.Storage.IConversationStore conversationStore,
            OpenClawNet.Agent.ISummaryService summaryService,
            OpenClawNet.Agent.IPromptComposer promptComposer,
            RuntimeModelSettings settings) =>
        {
            var sw = Stopwatch.StartNew();
            var steps = new List<object>();
            var sessionId = Guid.NewGuid();

            // Step 1: ConversationStore.AddMessageAsync
            try
            {
                var t0 = sw.ElapsedMilliseconds;
                await conversationStore.AddMessageAsync(sessionId, "user", "hi");
                steps.Add(new { step = "AddMessageAsync", ok = true, ms = sw.ElapsedMilliseconds - t0 });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "AddMessageAsync", ok = false, ms = sw.ElapsedMilliseconds, error = ex.Message });
                return Results.Ok(new { steps, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 2: ConversationStore.GetMessagesAsync
            try
            {
                var t0 = sw.ElapsedMilliseconds;
                var msgs = await conversationStore.GetMessagesAsync(sessionId);
                steps.Add(new { step = "GetMessagesAsync", ok = true, ms = sw.ElapsedMilliseconds - t0, count = msgs.Count });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "GetMessagesAsync", ok = false, ms = sw.ElapsedMilliseconds, error = ex.Message });
                return Results.Ok(new { steps, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 3: SummaryService.SummarizeIfNeededAsync
            try
            {
                var t0 = sw.ElapsedMilliseconds;
                var history = new List<OpenClawNet.Models.Abstractions.ChatMessage>
                {
                    new() { Role = ChatMessageRole.User, Content = "hi" }
                };
                var summary = await summaryService.SummarizeIfNeededAsync(sessionId, history);
                steps.Add(new { step = "SummarizeIfNeededAsync", ok = true, ms = sw.ElapsedMilliseconds - t0, hasSummary = summary != null });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "SummarizeIfNeededAsync", ok = false, ms = sw.ElapsedMilliseconds, error = ex.Message });
                return Results.Ok(new { steps, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 4: PromptComposer.ComposeAsync
            List<OpenClawNet.Models.Abstractions.ChatMessage>? composedMessages = null;
            try
            {
                var t0 = sw.ElapsedMilliseconds;
                var ctx = new OpenClawNet.Agent.PromptContext
                {
                    SessionId = sessionId,
                    UserMessage = "hi",
                    History = [],
                    SessionSummary = null
                };
                var msgs = await promptComposer.ComposeAsync(ctx);
                composedMessages = msgs.ToList();
                steps.Add(new { step = "ComposeAsync", ok = true, ms = sw.ElapsedMilliseconds - t0, messageCount = composedMessages.Count });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "ComposeAsync", ok = false, ms = sw.ElapsedMilliseconds, error = ex.Message });
                return Results.Ok(new { steps, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 5: ModelClient.StreamAsync with composed messages (no tools)
            try
            {
                var t0 = sw.ElapsedMilliseconds;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var request = new ChatRequest { Messages = composedMessages! };
                var chunks = new StringBuilder();
                var chunkCount = 0;
                await foreach (var chunk in modelClient.StreamAsync(request, cts.Token))
                {
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        chunks.Append(chunk.Content);
                        chunkCount++;
                    }
                }
                steps.Add(new { step = "StreamAsync_NoTools", ok = true, ms = sw.ElapsedMilliseconds - t0, chunkCount, contentLength = chunks.Length });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "StreamAsync_NoTools", ok = false, ms = sw.ElapsedMilliseconds, error = ex.Message, type = ex.GetType().Name });
                return Results.Ok(new { steps, totalMs = sw.ElapsedMilliseconds });
            }

            // Step 6: Full orchestrator.StreamAsync
            try
            {
                var t0 = sw.ElapsedMilliseconds;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var request = new OpenClawNet.Agent.AgentRequest
                {
                    SessionId = Guid.NewGuid(),
                    UserMessage = "hi"
                };
                var events = new List<string>();
                await foreach (var evt in orchestrator.StreamAsync(request, cts.Token))
                {
                    events.Add($"{evt.Type}: {evt.Content?.Substring(0, Math.Min(evt.Content.Length, 50))}");
                    if (events.Count > 20) break;
                }
                steps.Add(new { step = "Orchestrator_StreamAsync", ok = true, ms = sw.ElapsedMilliseconds - t0, eventCount = events.Count, events });
            }
            catch (OperationCanceledException)
            {
                steps.Add(new { step = "Orchestrator_StreamAsync", ok = false, ms = sw.ElapsedMilliseconds, error = "TIMED OUT after 30s — this is the hang!" });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "Orchestrator_StreamAsync", ok = false, ms = sw.ElapsedMilliseconds, error = ex.Message, type = ex.GetType().Name });
            }

            // Cleanup test session
            try { await conversationStore.DeleteSessionAsync(sessionId); } catch { }

            return Results.Ok(new { steps, totalMs = sw.ElapsedMilliseconds });
        })
        .WithName("ChatDebugPipeline")
        .WithTags("Debug")
        .WithDescription("Tests each pipeline step independently to isolate hangs");
    }
}
