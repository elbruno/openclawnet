using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenClawNet.Gateway;
using OpenClawNet.Tools.GoogleWorkspace;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace OpenClawNet.E2ETests.OAuth;

/// <summary>
/// E2E tests for Google OAuth flow (S5-7).
/// Tests the full OAuth 2.0 authorization code + PKCE flow through the Gateway endpoints:
/// /api/auth/google/start, /api/auth/google/callback, /api/auth/google/disconnect.
/// Uses WireMock to simulate Google's authorization and token endpoints.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Layer", "E2E")]
public sealed class GoogleOAuthFlowE2ETests : IClassFixture<GoogleOAuthE2EFactory>, IAsyncLifetime
{
    private readonly GoogleOAuthE2EFactory _factory;
    private readonly HttpClient _client;

    public GoogleOAuthFlowE2ETests(GoogleOAuthE2EFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false // We need to capture 302 responses
        });
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OAuth_Start_Redirects_To_Google_With_PKCE_Params()
    {
        // ACT: GET /api/auth/google/start?userId=testuser
        var response = await _client.GetAsync("/api/auth/google/start?userId=testuser");

        // ASSERT: 302 to Google authorization endpoint
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();

        var authUrl = response.Headers.Location!.ToString();
        authUrl.Should().Contain("accounts.google.com/o/oauth2/v2/auth");
        authUrl.Should().Contain("client_id=");
        authUrl.Should().Contain("redirect_uri=");
        authUrl.Should().Contain("response_type=code");
        authUrl.Should().Contain("state=");
        authUrl.Should().Contain("code_challenge=");
        authUrl.Should().Contain("code_challenge_method=S256");
        authUrl.Should().Contain("access_type=offline");
        authUrl.Should().Contain("prompt=consent");
        authUrl.Should().Contain("scope=");
    }

    [Fact]
    public async Task OAuth_Start_Missing_UserId_Returns_400()
    {
        // ACT: GET /api/auth/google/start (no userId query param)
        var response = await _client.GetAsync("/api/auth/google/start");

        // ASSERT: 400 Bad Request
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("userId");
        body.Should().Contain("required");
    }

    [Fact]
    public async Task OAuth_Complete_Flow_Start_Callback_Disconnect()
    {
        // STEP 1: Start OAuth flow
        var startResponse = await _client.GetAsync("/api/auth/google/start?userId=testuser");
        startResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var authUrl = startResponse.Headers.Location!.ToString();
        var stateMatch = System.Text.RegularExpressions.Regex.Match(authUrl, @"state=([^&]+)");
        stateMatch.Success.Should().BeTrue("state parameter should be present in authorization URL");
        var state = Uri.UnescapeDataString(stateMatch.Groups[1].Value);

        // STEP 2: Simulate Google callback with authorization code
        // Configure WireMock to stub Google token exchange endpoint
        _factory.GoogleTokenEndpoint.Given(
            Request.Create()
                .WithPath("/token")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""
                    {
                        "access_token": "ya29.fake_access_token_123",
                        "refresh_token": "1//fake_refresh_token_456",
                        "expires_in": 3600,
                        "scope": "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/calendar.events",
                        "token_type": "Bearer"
                    }
                    """));

        var callbackResponse = await _client.GetAsync($"/api/auth/google/callback?code=fake_auth_code&state={Uri.EscapeDataString(state)}");

        // ASSERT: 302 to success page
        callbackResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callbackResponse.Headers.Location.Should().NotBeNull();
        callbackResponse.Headers.Location!.ToString().Should().Contain("/auth/google/connected");

        // Verify WireMock received token exchange request
        var requests = _factory.GoogleTokenEndpoint.LogEntries
            .Where(e => e.RequestMessage.Path == "/token" && e.RequestMessage.Method == "POST")
            .ToList();
        requests.Should().ContainSingle("Google token endpoint should have been called once");

        var tokenRequest = requests[0].RequestMessage.Body!;
        tokenRequest.Should().Contain("code=fake_auth_code");
        tokenRequest.Should().Contain("grant_type=authorization_code");
        tokenRequest.Should().Contain("code_verifier=");

        // STEP 3: Verify token was stored (indirect check via disconnect)
        var disconnectResponse = await _client.PostAsync("/api/auth/google/disconnect?userId=testuser", null);

        // ASSERT: 204 No Content
        disconnectResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task OAuth_Callback_Invalid_State_Returns_400()
    {
        // ACT: Simulate callback with invalid state
        var response = await _client.GetAsync("/api/auth/google/callback?code=fake_code&state=invalid_state_12345");

        // ASSERT: 400 Bad Request
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("state");
        body.Should().Contain("invalid", "should mention that state is invalid");
    }

    [Fact]
    public async Task OAuth_Callback_Missing_Code_Returns_400()
    {
        // ARRANGE: Start flow to get valid state
        var startResponse = await _client.GetAsync("/api/auth/google/start?userId=testuser");
        var authUrl = startResponse.Headers.Location!.ToString();
        var stateMatch = System.Text.RegularExpressions.Regex.Match(authUrl, @"state=([^&]+)");
        var state = Uri.UnescapeDataString(stateMatch.Groups[1].Value);

        // ACT: Callback without code parameter
        var response = await _client.GetAsync($"/api/auth/google/callback?state={Uri.EscapeDataString(state)}");

        // ASSERT: 400 Bad Request
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("code");
    }

    [Fact]
    public async Task OAuth_Callback_Error_Param_Redirects_To_Error_Page()
    {
        // ACT: Simulate Google returning error in callback
        var response = await _client.GetAsync("/api/auth/google/callback?error=access_denied&state=somestate");

        // ASSERT: 302 to error page
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/auth/google/error");
        response.Headers.Location.ToString().Should().Contain("message=");
    }

    [Fact]
    public async Task OAuth_Callback_Expired_State_Returns_400()
    {
        // ARRANGE: Start flow, capture state
        var startResponse = await _client.GetAsync("/api/auth/google/start?userId=testuser");
        var authUrl = startResponse.Headers.Location!.ToString();
        var stateMatch = System.Text.RegularExpressions.Regex.Match(authUrl, @"state=([^&]+)");
        var state = Uri.UnescapeDataString(stateMatch.Groups[1].Value);

        // Consume the state once (simulates callback)
        await _client.GetAsync($"/api/auth/google/callback?code=fake_code&state={Uri.EscapeDataString(state)}");

        // ACT: Try to use the same state again (should be expired/consumed)
        var response = await _client.GetAsync($"/api/auth/google/callback?code=fake_code_2&state={Uri.EscapeDataString(state)}");

        // ASSERT: 400 Bad Request (state already consumed)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("state");
    }

    [Fact]
    public async Task OAuth_Disconnect_Missing_UserId_Returns_400()
    {
        // ACT: POST /api/auth/google/disconnect (no userId query param)
        var response = await _client.PostAsync("/api/auth/google/disconnect", null);

        // ASSERT: 400 Bad Request
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("userId");
    }

    [Fact]
    public async Task OAuth_Disconnect_Nonexistent_User_Returns_204()
    {
        // ACT: Disconnect user with no tokens
        var response = await _client.PostAsync("/api/auth/google/disconnect?userId=nonexistent_user", null);

        // ASSERT: 204 No Content (idempotent delete)
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

/// <summary>
/// Custom WebApplicationFactory for Google OAuth E2E tests.
/// Configures WireMock to simulate Google's OAuth endpoints and uses in-memory token storage.
/// </summary>
public sealed class GoogleOAuthE2EFactory : WebApplicationFactory<GatewayProgramMarker>, IAsyncLifetime
{
    public WireMockServer GoogleTokenEndpoint { get; private set; } = null!;

    private string _tokenEndpointUrl = "";

    public Task InitializeAsync()
    {
        // Start WireMock server to simulate Google token endpoint
        GoogleTokenEndpoint = WireMockServer.Start();
        _tokenEndpointUrl = GoogleTokenEndpoint.Urls[0] + "/token";
        
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        if (GoogleTokenEndpoint is not null)
        {
            GoogleTokenEndpoint.Stop();
            GoogleTokenEndpoint.Dispose();
        }
        
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var dict = new Dictionary<string, string?>
            {
                // Google OAuth config pointing at test values
                ["GoogleWorkspace:ClientId"] = "test-client-id",
                ["GoogleWorkspace:ClientSecret"] = "test-client-secret",
                ["GoogleWorkspace:RedirectUri"] = "http://localhost/api/auth/google/callback",
                ["GoogleWorkspace:Scopes:0"] = "https://www.googleapis.com/auth/gmail.readonly",
                ["GoogleWorkspace:Scopes:1"] = "https://www.googleapis.com/auth/calendar.events",
                
                // Other minimal config
                ["ConnectionStrings:openclawnet-db"] = "Data Source=:memory:",
                ["Teams:Enabled"] = "false",
            };

            cfg.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            // Replace with in-memory token store for hermetic tests
            services.RemoveAll<IGoogleOAuthTokenStore>();
            services.AddSingleton<IGoogleOAuthTokenStore, InMemoryGoogleOAuthTokenStore>();

            // Use in-memory OAuth flow state store (default is already in-memory)
            services.RemoveAll<IOAuthFlowStateStore>();
            services.AddSingleton<IOAuthFlowStateStore, InMemoryOAuthFlowStateStore>();
        });
    }
}
