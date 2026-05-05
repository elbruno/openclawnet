using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenClawNet.PlaywrightTests;

/// <summary>
/// API-level E2E tests for the gateway service — covers gateway-only demos 01–08.
/// Uses HttpClient directly (no browser needed).
/// </summary>
[Collection("AppHost")]
public class GatewayApiTests : IAsyncLifetime
{
    private readonly AppHostFixture _fixture;
    private HttpClient _client = null!;

    public GatewayApiTests(AppHostFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _client = _fixture.CreateGatewayHttpClient();
        _client.Timeout = TimeSpan.FromMinutes(3);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that the response is either 200 OK (model available) or 503 Service Unavailable
    /// (model not available — graceful degradation). A 500 still fails.
    /// Returns true if the model was available (200), false if unavailable (503).
    /// </summary>
    private static bool AssertSuccessOrServiceUnavailable(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return false; // Model not available — acceptable, not a bug

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return true;
    }

    // ── Demo 01: Health & Version ─────────────────────────────────────────────

    [Fact]
    public async Task Health_GetEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", json.GetProperty("status").GetString());
        Assert.True(json.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task Version_GetEndpoint_ReturnsVersionInfo()
    {
        var response = await _client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        Assert.Equal("OpenClawNet", json.GetProperty("name").GetString());
    }

    // ── Demo 02: Session + Chat ───────────────────────────────────────────────

    [Fact]
    public async Task Sessions_CreateSession_ReturnsCreated()
    {
        var response = await _client.PostAsJsonAsync("/api/sessions", new { title = "E2E Test Session" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("id", out var id));
        Assert.NotEqual(Guid.Empty.ToString(), id.GetString());
        Assert.Equal("E2E Test Session", json.GetProperty("title").GetString());
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Chat_SendMessage_ReturnsResponseWithContent()
    {
        // Create session first
        var sessionResp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Chat E2E" });
        var sessionJson = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionJson.GetProperty("id").GetString();

        // Send chat message
        var chatResp = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "What is .NET Aspire in one sentence?"
        });

        if (!AssertSuccessOrServiceUnavailable(chatResp))
            return; // Model unavailable — session created, chat can't complete

        var chatJson = await chatResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(chatJson.GetProperty("content").GetString()));
        Assert.True(chatJson.TryGetProperty("toolCallCount", out _));
        Assert.True(chatJson.TryGetProperty("totalTokens", out _));
    }

    // ── Demo 03: Multi-turn Conversation ──────────────────────────────────────

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task MultiTurn_CreateChatRenameDelete_FullLifecycle()
    {
        // Create session
        var sessionResp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Multi-turn E2E" });
        var sessionJson = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionJson.GetProperty("id").GetString();

        // Turn 1
        var turn1 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "Remember: my favorite color is blue."
        });

        if (!AssertSuccessOrServiceUnavailable(turn1))
        {
            // Model unavailable — skip chat turns, still validate rename + delete
            goto SessionOps;
        }

        // Turn 2
        var turn2 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "What is my favorite color?"
        });
        AssertSuccessOrServiceUnavailable(turn2);

        // Get messages — should have user + assistant pairs
        var messagesResp = await _client.GetAsync($"/api/sessions/{sessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, messagesResp.StatusCode);
        var messages = await messagesResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(messages.GetArrayLength() >= 4, "Expected at least 4 messages (2 user + 2 assistant)");

        SessionOps:

        // Rename session
        var renameResp = await _client.PatchAsJsonAsync($"/api/sessions/{sessionId}/title", new { title = "Renamed Session" });
        Assert.Equal(HttpStatusCode.OK, renameResp.StatusCode);
        var renamed = await renameResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed Session", renamed.GetProperty("title").GetString());

        // Delete session
        var deleteResp = await _client.DeleteAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task Sessions_ListSessions_ReturnsArray()
    {
        // Create a session to ensure there's at least one
        await _client.PostAsJsonAsync("/api/sessions", new { title = "List Test" });

        var response = await _client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetArrayLength() >= 1);
    }

    // ── Demo 04: Tools ────────────────────────────────────────────────────────

    [Fact]
    public async Task Tools_GetList_ReturnsRegisteredTools()
    {
        var response = await _client.GetAsync("/api/tools");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetArrayLength() > 0, "Expected at least one registered tool");

        // Verify tool shape
        var first = json[0];
        Assert.True(first.TryGetProperty("name", out _));
        Assert.True(first.TryGetProperty("description", out _));
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Chat_FileQuestion_TriggersToolUse()
    {
        var sessionResp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Tool Use E2E" });
        var sessionJson = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionJson.GetProperty("id").GetString();

        var chatResp = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "List the top-level files and folders in the current directory."
        });

        if (!AssertSuccessOrServiceUnavailable(chatResp))
            return; // Model unavailable — can't verify tool use

        var chatJson = await chatResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(chatJson.GetProperty("toolCallCount").GetInt32() > 0,
            "Expected toolCallCount > 0 for a file-system question");
    }

    // ── Demo 05: Skills ───────────────────────────────────────────────────────

    [Fact]
    public async Task Skills_GetList_ReturnsSkills()
    {
        var response = await _client.GetAsync("/api/skills");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetArrayLength() >= 0, "Skills endpoint should return an array");
    }

    [Fact]
    public async Task Skills_EnableDisable_TogglesState()
    {
        // Get initial skills list to find one to toggle
        var listResp = await _client.GetAsync("/api/skills");
        var skills = await listResp.Content.ReadFromJsonAsync<JsonElement>();

        if (skills.GetArrayLength() == 0)
            return; // No skills installed — skip gracefully

        var skillName = skills[0].GetProperty("name").GetString()!;

        // Disable
        var disableResp = await _client.PostAsync($"/api/skills/{skillName}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResp.StatusCode);
        var disabled = await disableResp.Content.ReadFromJsonAsync<JsonElement>();
        // Check that the skill has enabledByAgent entries and all are false
        var enabledByAgent = disabled.GetProperty("enabledByAgent");
        Assert.True(enabledByAgent.EnumerateObject().Any(), "Expected at least one agent mapping");
        Assert.All(enabledByAgent.EnumerateObject(), kvp => Assert.False(kvp.Value.GetBoolean(), $"Expected {kvp.Name} to be disabled"));

        // Re-enable
        var enableResp = await _client.PostAsync($"/api/skills/{skillName}/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);
        var enabled = await enableResp.Content.ReadFromJsonAsync<JsonElement>();
        // Check that all agents now have the skill enabled
        var enabledByAgent2 = enabled.GetProperty("enabledByAgent");
        Assert.True(enabledByAgent2.EnumerateObject().Any(), "Expected at least one agent mapping");
        Assert.All(enabledByAgent2.EnumerateObject(), kvp => Assert.True(kvp.Value.GetBoolean(), $"Expected {kvp.Name} to be enabled"));
    }

    // ── Demo 06: SignalR Hub Connectivity (deprecated — chat now uses HTTP NDJSON) ──

    [Fact]
    public async Task SignalR_ChatHub_EndpointIsReachable()
    {
        // NOTE: The ChatHub is [Obsolete] — chat now uses POST /api/chat/stream (NDJSON).
        // This test validates the legacy endpoint still responds while it exists.
        var response = await _client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null);

        // SignalR negotiate returns 200 with connection info
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("connectionId", out _) || json.TryGetProperty("connectionToken", out _),
            "SignalR negotiate should return connection info");
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task ChatStream_PostEndpoint_ReturnsNdjsonResponse()
    {
        // Create session first
        var sessionResp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Stream E2E" });
        var sessionJson = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionJson.GetProperty("id").GetString();

        // POST to the NDJSON streaming endpoint
        var streamResp = await _client.PostAsJsonAsync("/api/chat/stream", new
        {
            sessionId,
            message = "Say hello in one word."
        });

        if (!AssertSuccessOrServiceUnavailable(streamResp))
            return; // Model unavailable — endpoint exists but can't stream

        // Verify we got a response (content type may be application/x-ndjson or application/json)
        var content = await streamResp.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(content), "Stream response should not be empty");
    }

    // ── Model Providers API ───────────────────────────────────────────────────

    [Fact]
    public async Task ModelProviders_GetList_ReturnsArray()
    {
        var response = await _client.GetAsync("/api/model-providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.True(json.GetArrayLength() >= 1, "Expected at least 1 seeded provider");

        // Verify provider shape
        var first = json[0];
        Assert.True(first.TryGetProperty("name", out _), "Provider should have a name");
        Assert.True(first.TryGetProperty("providerType", out _) || first.TryGetProperty("provider", out _),
            "Provider should have a type");
    }

    // ── Demo 08: Webhooks ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Webhooks_PostEvent_CreatesSession()
    {
        var payload = new
        {
            message = "Deployment complete",
            data = new { environment = "staging", version = "1.0.0", status = "success" }
        };

        var response = await _client.PostAsJsonAsync("/api/webhooks/deploy-complete", payload);

        if (!AssertSuccessOrServiceUnavailable(response))
            return; // Model unavailable — webhook received but agent can't respond

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deploy-complete", json.GetProperty("eventType").GetString());
        Assert.True(json.TryGetProperty("sessionId", out _));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("agentResponse").GetString()));
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Webhooks_GetList_ReturnsWebhookSessions()
    {
        // Fire a webhook first to ensure there's at least one
        var fireResp = await _client.PostAsJsonAsync("/api/webhooks/test-event", new
        {
            message = "Test webhook for listing"
        });

        // If model unavailable, the webhook can't produce a full session
        if (fireResp.StatusCode == HttpStatusCode.ServiceUnavailable)
            return;

        var response = await _client.GetAsync("/api/webhooks");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetArrayLength() >= 1, "Expected at least one webhook session");
    }

    // ── Demo 01: OpenAPI Spec ─────────────────────────────────────────────────

    [Fact]
    public async Task OpenApi_GetSpec_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("paths", out var paths), "OpenAPI spec should contain paths");
        Assert.True(paths.EnumerateObject().Any(), "OpenAPI spec should have at least one path");
    }

    // ── Demo 03: Session By ID + Message History ──────────────────────────────

    [Fact]
    public async Task Sessions_GetById_ReturnsFullSession()
    {
        // Create a session
        var createResp = await _client.PostAsJsonAsync("/api/sessions", new { title = "GetById E2E" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = created.GetProperty("id").GetString();

        // Fetch by ID
        var getResp = await _client.GetAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var session = await getResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(sessionId, session.GetProperty("id").GetString());
        Assert.Equal("GetById E2E", session.GetProperty("title").GetString());
        Assert.True(session.TryGetProperty("createdAt", out _), "Session should have createdAt");
        Assert.True(session.TryGetProperty("messages", out _), "Full session should include messages array");
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Sessions_GetMessages_ReturnsHistory()
    {
        // Create session
        var createResp = await _client.PostAsJsonAsync("/api/sessions", new { title = "Messages History E2E" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = created.GetProperty("id").GetString();

        // Send first message
        var chat1 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "Remember: the project name is OpenClawNet."
        });
        if (!AssertSuccessOrServiceUnavailable(chat1))
            return;

        // Send second message
        var chat2 = await _client.PostAsJsonAsync("/api/chat", new
        {
            sessionId,
            message = "What is the project name I just told you?"
        });
        AssertSuccessOrServiceUnavailable(chat2);

        // Fetch message history
        var messagesResp = await _client.GetAsync($"/api/sessions/{sessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, messagesResp.StatusCode);
        var messages = await messagesResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(messages.GetArrayLength() >= 4,
            $"Expected at least 4 messages (2 user + 2 assistant), got {messages.GetArrayLength()}");

        // Verify ordering: first message should be a user role
        var firstMsg = messages[0];
        Assert.Equal("user", firstMsg.GetProperty("role").GetString());
    }

    // ── Demo 04/05: Skills Reload ─────────────────────────────────────────────

    [Fact]
    public async Task Skills_Reload_ReturnsSuccess()
    {
        var response = await _client.PostAsync("/api/skills/reload", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("reloaded").GetBoolean(), "Expected reloaded=true");
        Assert.True(json.TryGetProperty("count", out _), "Response should include skill count");
    }

    // ── Demo 06: Settings Provider Switch ─────────────────────────────────────

    [Fact]
    public async Task Settings_UpdateProvider_ChangesRuntime()
    {
        // Capture current settings
        var getResp = await _client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var original = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var originalProvider = original.GetProperty("provider").GetString();

        // Update to a different provider config
        var newProvider = originalProvider == "ollama" ? "azure-openai" : "ollama";
        var putResp = await _client.PutAsJsonAsync("/api/settings", new
        {
            provider = newProvider,
            model = "test-model",
            endpoint = "http://localhost:11434"
        });
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        // Verify the change took effect
        var verifyResp = await _client.GetAsync("/api/settings");
        var updated = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(newProvider, updated.GetProperty("provider").GetString());

        // Restore original settings
        await _client.PutAsJsonAsync("/api/settings", new
        {
            provider = originalProvider,
            model = original.GetProperty("model").GetString(),
            endpoint = original.GetProperty("endpoint").GetString()
        });
    }

    // ── Demo 05/08: Webhook Payloads ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Webhooks_CodeReviewPayload_CreatesSession()
    {
        var payload = new
        {
            message = "Review requested on PR #42",
            data = new { pr_number = 42, action = "review_requested" }
        };

        var response = await _client.PostAsJsonAsync("/api/webhooks/pull_request_review", payload);

        if (!AssertSuccessOrServiceUnavailable(response))
            return;

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pull_request_review", json.GetProperty("eventType").GetString());
        Assert.True(json.TryGetProperty("sessionId", out _));
    }

    [Fact]
    [Trait("Category", "RequiresModel")]
    public async Task Webhooks_GitPushPayload_CreatesSession()
    {
        var payload = new
        {
            message = "3 commits pushed to main",
            data = new { branch = "main", commits = 3 }
        };

        var response = await _client.PostAsJsonAsync("/api/webhooks/push", payload);

        if (!AssertSuccessOrServiceUnavailable(response))
            return;

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("push", json.GetProperty("eventType").GetString());
        Assert.True(json.TryGetProperty("sessionId", out _));
    }
}
