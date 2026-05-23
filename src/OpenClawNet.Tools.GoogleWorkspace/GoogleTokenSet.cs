namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Represents a complete set of Google OAuth tokens for a user.
/// </summary>
/// <param name="AccessToken">OAuth access token (short-lived).</param>
/// <param name="RefreshToken">OAuth refresh token (long-lived, used to obtain new access tokens).</param>
/// <param name="ExpiresAtUtc">UTC timestamp when the access token expires.</param>
/// <param name="Scopes">Space-separated list of granted OAuth scopes.</param>
public sealed record GoogleTokenSet(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string Scopes);
