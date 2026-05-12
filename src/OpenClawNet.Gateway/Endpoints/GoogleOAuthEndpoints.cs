using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Gateway.Endpoints;

/// <summary>
/// Google OAuth 2.0 web flow endpoints with PKCE.
/// Implements S5-4: authorization initiation, callback handling, and disconnect.
/// </summary>
public static class GoogleOAuthEndpoints
{
    private static readonly ActivitySource ActivitySource = new("OpenClawNet.Tools.GoogleWorkspace");

    public static void MapGoogleOAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth/google").WithTags("Google OAuth");

        // GET /api/auth/google/start?userId={userId}
        // Initiates OAuth flow with PKCE
        group.MapGet("/start", StartOAuthFlow)
            .WithName("StartGoogleOAuth")
            .WithDescription("Initiates Google OAuth 2.0 authorization flow with PKCE. Redirects to Google consent screen.");

        // GET /api/auth/google/callback?code={code}&state={state}&error={error}
        // Handles OAuth callback from Google
        group.MapGet("/callback", HandleOAuthCallback)
            .WithName("GoogleOAuthCallback")
            .WithDescription("OAuth 2.0 callback endpoint. Exchanges authorization code for tokens.");

        // POST /api/auth/google/disconnect?userId={userId}
        // Revokes OAuth tokens
        group.MapPost("/disconnect", DisconnectGoogle)
            .WithName("DisconnectGoogle")
            .WithDescription("Revokes Google OAuth tokens and deletes local credentials.");
    }

    private static async Task<IResult> StartOAuthFlow(
        [FromQuery] string? userId,
        [FromServices] OpenClawNet.Tools.GoogleWorkspace.IOAuthFlowStateStore flowStateStore,
        [FromServices] IOptions<OpenClawNet.Tools.GoogleWorkspace.GoogleWorkspaceOptions> options,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("OAuthStart");
        activity?.SetTag("userId", userId);
        activity?.SetTag("oauth.flow_step", "start");

        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("OAuth start attempted with missing userId");
            return Results.BadRequest(new { error = "userId parameter is required" });
        }

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ClientId) || string.IsNullOrWhiteSpace(opts.RedirectUri))
        {
            logger.LogError("Google OAuth not configured: ClientId or RedirectUri missing");
            return Results.Problem(
                "Google OAuth is not configured. Administrator must set ClientId and RedirectUri.",
                statusCode: 500);
        }

        try
        {
            // Generate PKCE code_verifier (43-128 chars, URL-safe)
            var codeVerifierBytes = new byte[32]; // 32 bytes = 43 chars in base64url
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(codeVerifierBytes);
            }
            var codeVerifier = Convert.ToBase64String(codeVerifierBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");

            // Generate PKCE code_challenge = base64url(SHA256(code_verifier))
            var codeChallenge = ComputeCodeChallenge(codeVerifier);

            // Store state + code_verifier server-side (10-min TTL)
            var state = await flowStateStore.StoreAsync(userId, codeVerifier, ct);

            // Build Google authorization URL
            var scopes = string.Join(" ", opts.Scopes);
            var authUrl = new StringBuilder(opts.AuthorizationEndpoint);
            authUrl.Append($"?client_id={Uri.EscapeDataString(opts.ClientId)}");
            authUrl.Append($"&redirect_uri={Uri.EscapeDataString(opts.RedirectUri)}");
            authUrl.Append("&response_type=code");
            authUrl.Append($"&scope={Uri.EscapeDataString(scopes)}");
            authUrl.Append($"&state={Uri.EscapeDataString(state)}");
            authUrl.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge)}");
            authUrl.Append("&code_challenge_method=S256");
            authUrl.Append("&access_type=offline"); // Request refresh token
            authUrl.Append("&prompt=consent"); // Force consent to ensure refresh token issuance

            logger.LogInformation(
                "OAuth flow started for user {UserId}, redirecting to Google authorization endpoint",
                userId);

            return Results.Redirect(authUrl.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start OAuth flow for user {UserId}", userId);
            return Results.Problem("Failed to initiate OAuth flow", statusCode: 500);
        }
    }

    private static async Task<IResult> HandleOAuthCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromServices] OpenClawNet.Tools.GoogleWorkspace.IOAuthFlowStateStore flowStateStore,
        [FromServices] OpenClawNet.Tools.GoogleWorkspace.IGoogleOAuthTokenStore tokenStore,
        [FromServices] IOptions<OpenClawNet.Tools.GoogleWorkspace.GoogleWorkspaceOptions> options,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("OAuthCallback");
        activity?.SetTag("oauth.flow_step", "callback");

        // Handle OAuth error response
        if (!string.IsNullOrWhiteSpace(error))
        {
            logger.LogWarning("OAuth callback received error: {Error}", error);
            // Redirect to UI error page (sanitized — don't expose raw error_description)
            return Results.Redirect($"/auth/google/error?message={Uri.EscapeDataString("Authorization failed")}");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            logger.LogWarning("OAuth callback missing code or state parameter");
            return Results.BadRequest(new { error = "Missing code or state parameter" });
        }

        // Consume state (one-shot, check expiry)
        var flowState = await flowStateStore.ConsumeAsync(state, ct);
        if (flowState is null)
        {
            logger.LogWarning("OAuth callback with invalid or expired state parameter");
            return Results.BadRequest(new { error = "invalid or expired state parameter" });
        }

        var userId = flowState.UserId;
        activity?.SetTag("userId", userId);

        var opts = options.Value;

        try
        {
            // Exchange authorization code + code_verifier for tokens
            var httpClient = httpClientFactory.CreateClient();
            var tokenRequest = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret,
                ["redirect_uri"] = opts.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = flowState.CodeVerifier
            };

            var response = await httpClient.PostAsync(
                opts.TokenEndpoint,
                new FormUrlEncodedContent(tokenRequest),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError(
                    "Google token exchange failed: {StatusCode} {ReasonPhrase}",
                    response.StatusCode,
                    response.ReasonPhrase);
                return Results.Problem("Failed to exchange authorization code for tokens", statusCode: 500);
            }

            var tokenResponseJson = await response.Content.ReadAsStringAsync(ct);
            using var tokenDoc = JsonDocument.Parse(tokenResponseJson);
            var root = tokenDoc.RootElement;

            var accessToken = root.GetProperty("access_token").GetString()!;
            var refreshToken = root.GetProperty("refresh_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            var scope = root.GetProperty("scope").GetString() ?? "";

            var tokenSet = new OpenClawNet.Tools.GoogleWorkspace.GoogleTokenSet(
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                Scopes: scope);

            // Persist tokens
            await tokenStore.SaveTokenAsync(userId, tokenSet, ct);

            logger.LogInformation(
                "OAuth tokens obtained and saved for user {UserId}",
                userId);

            // Redirect to UI success page
            return Results.Redirect("/auth/google/connected");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth callback processing failed for user {UserId}", userId);
            return Results.Problem("Failed to complete OAuth flow", statusCode: 500);
        }
    }

    private static async Task<IResult> DisconnectGoogle(
        [FromQuery] string? userId,
        [FromServices] OpenClawNet.Tools.GoogleWorkspace.IGoogleOAuthTokenStore tokenStore,
        [FromServices] IOptions<OpenClawNet.Tools.GoogleWorkspace.GoogleWorkspaceOptions> options,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("OAuthDisconnect");
        activity?.SetTag("userId", userId);
        activity?.SetTag("oauth.flow_step", "disconnect");

        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("OAuth disconnect attempted with missing userId");
            return Results.BadRequest(new { error = "userId parameter is required" });
        }

        try
        {
            // Retrieve tokens
            var tokenSet = await tokenStore.GetTokenAsync(userId, ct);

            // Delete local tokens (even if revocation fails)
            await tokenStore.DeleteTokenAsync(userId, ct);

            // Best-effort: revoke refresh token at Google
            if (tokenSet?.RefreshToken is not null)
            {
                try
                {
                    var httpClient = httpClientFactory.CreateClient();
                    var revokeUrl = $"{options.Value.RevokeEndpoint}?token={Uri.EscapeDataString(tokenSet.RefreshToken)}";
                    var revokeResponse = await httpClient.PostAsync(revokeUrl, null, ct);

                    if (revokeResponse.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Google refresh token revoked for user {UserId}", userId);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Google token revocation returned non-success status {StatusCode} for user {UserId}",
                            revokeResponse.StatusCode,
                            userId);
                    }
                }
                catch (Exception ex)
                {
                    // Don't fail disconnect if revocation fails — local delete is what matters
                    logger.LogWarning(ex, "Failed to revoke Google token for user {UserId}", userId);
                }
            }

            logger.LogInformation("Google OAuth disconnected for user {UserId}", userId);
            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to disconnect Google OAuth for user {UserId}", userId);
            return Results.Problem("Failed to disconnect Google OAuth", statusCode: 500);
        }
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var bytes = Encoding.UTF8.GetBytes(codeVerifier);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
