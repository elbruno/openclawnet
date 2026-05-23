using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Production implementation of IGoogleClientFactory.
/// Creates authenticated Google service instances using OAuth tokens from the token store.
/// Handles automatic token refresh when access token expires.
/// </summary>
public sealed class GoogleClientFactory : IGoogleClientFactory
{
    private const string ApplicationName = "OpenClawNet";
    private const int RefreshWindowSeconds = 60; // Refresh if token expires within 60 seconds
    
    private readonly IGoogleOAuthTokenStore _tokenStore;
    private readonly GoogleWorkspaceOptions _options;
    private readonly ILogger<GoogleClientFactory> _logger;
    private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;
    private readonly HttpMessageHandler? _messageHandler;
    private readonly Uri? _serviceBaseUri;

    public GoogleClientFactory(
        IGoogleOAuthTokenStore tokenStore,
        IOptions<GoogleWorkspaceOptions> options,
        ILogger<GoogleClientFactory> logger,
        System.Net.Http.IHttpClientFactory httpClientFactory,
        HttpMessageHandler? messageHandler = null,
        Uri? serviceBaseUri = null)
    {
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _messageHandler = messageHandler;
        _serviceBaseUri = serviceBaseUri;
    }

    public async Task<GmailService> CreateGmailServiceAsync(string userId, CancellationToken cancellationToken)
    {
        var credential = await GetUserCredentialAsync(userId, cancellationToken);
        
        return new GmailService(CreateServiceInitializer(credential));
    }

    public async Task<CalendarService> CreateCalendarServiceAsync(string userId, CancellationToken cancellationToken)
    {
        var credential = await GetUserCredentialAsync(userId, cancellationToken);
        
        return new CalendarService(CreateServiceInitializer(credential));
    }

    private BaseClientService.Initializer CreateServiceInitializer(UserCredential credential)
    {
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        };

        if (_messageHandler is not null)
        {
            initializer.HttpClientFactory = new HttpClientFromMessageHandlerFactory(_ =>
                new HttpClientFromMessageHandlerFactory.ConfiguredHttpMessageHandler(
                    _messageHandler,
                    performsAutomaticDecompression: false,
                    handlesRedirect: false));
        }

        if (_serviceBaseUri is not null)
        {
            initializer.BaseUri = _serviceBaseUri.ToString();
        }

        return initializer;
    }

    private async Task<UserCredential> GetUserCredentialAsync(string userId, CancellationToken cancellationToken)
    {
        var tokenSet = await _tokenStore.GetTokenAsync(userId, cancellationToken);
        
        if (tokenSet is null)
        {
            _logger.LogError("No OAuth tokens found for user {UserId}", userId);
            throw new OAuthRequiredException(
                userId,
                $"User has not authorized Google. Direct them to /api/auth/google/start?userId={Uri.EscapeDataString(userId)}");
        }

        // Check if token needs refresh (expires within 60 seconds)
        if (tokenSet.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(RefreshWindowSeconds))
        {
            _logger.LogInformation("Access token expired or expiring soon for user {UserId}, refreshing", userId);
            tokenSet = await RefreshTokenAsync(userId, tokenSet, cancellationToken);
        }

        // Convert token set to Google's TokenResponse format
        var tokenResponse = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            AccessToken = tokenSet.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(tokenSet.RefreshToken) ? null : tokenSet.RefreshToken,
            ExpiresInSeconds = (long)(tokenSet.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds,
            IssuedUtc = DateTime.UtcNow,
            Scope = tokenSet.Scopes
        };

        // Create UserCredential (Google's credential abstraction)
        var credential = new UserCredential(
            new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = _options.ClientId,
                        ClientSecret = _options.ClientSecret
                    },
                    Scopes = _options.Scopes
                }),
            userId,
            tokenResponse);

        return credential;
    }

    private async Task<GoogleTokenSet> RefreshTokenAsync(
        string userId,
        GoogleTokenSet currentTokenSet,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _messageHandler is null
                ? _httpClientFactory.CreateClient()
                : new HttpClient(_messageHandler, disposeHandler: false);
            var refreshRequest = new Dictionary<string, string>
            {
                ["refresh_token"] = currentTokenSet.RefreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["grant_type"] = "refresh_token"
            };

            var response = await httpClient.PostAsync(
                _options.TokenEndpoint,
                new FormUrlEncodedContent(refreshRequest),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Google token refresh failed for user {UserId}: {StatusCode} {ReasonPhrase}",
                    userId,
                    response.StatusCode,
                    response.ReasonPhrase);
                throw new OAuthRequiredException(
                    userId,
                    $"Failed to refresh Google OAuth token. User may need to re-authorize at /api/auth/google/start?userId={Uri.EscapeDataString(userId)}");
            }

            var tokenResponseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var tokenDoc = JsonDocument.Parse(tokenResponseJson);
            var root = tokenDoc.RootElement;

            var newAccessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            
            // Google may or may not return a new refresh token during refresh
            var newRefreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElem)
                ? refreshTokenElem.GetString()!
                : currentTokenSet.RefreshToken;

            var newScope = root.TryGetProperty("scope", out var scopeElem)
                ? scopeElem.GetString()!
                : currentTokenSet.Scopes;

            var newTokenSet = new GoogleTokenSet(
                AccessToken: newAccessToken,
                RefreshToken: newRefreshToken,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                Scopes: newScope);

            // Persist refreshed tokens
            await _tokenStore.SaveTokenAsync(userId, newTokenSet, cancellationToken);

            _logger.LogInformation(
                "OAuth tokens refreshed successfully for user {UserId}, new expiry: {ExpiresAt}",
                userId,
                newTokenSet.ExpiresAtUtc);

            return newTokenSet;
        }
        catch (OAuthRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refreshing OAuth token for user {UserId}", userId);
            throw new OAuthRequiredException(
                userId,
                $"Failed to refresh Google OAuth token: {ex.Message}",
                ex);
        }
    }
}
