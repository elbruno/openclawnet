namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Configuration options for Google Workspace integration.
/// </summary>
public sealed class GoogleWorkspaceOptions
{
    /// <summary>
    /// Configuration section name for GoogleWorkspace options.
    /// </summary>
    public const string SectionName = "GoogleWorkspace";

    /// <summary>
    /// OAuth client ID from Google Cloud Console.
    /// Should be stored via user-secrets or environment variables, not committed to source control.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// OAuth client secret from Google Cloud Console.
    /// MUST be stored via user-secrets or secure configuration, never committed to git.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// OAuth redirect URI for web flow callback (e.g., "https://localhost:5001/api/auth/google/callback").
    /// Must match exactly with the URI registered in Google Cloud Console.
    /// </summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>
    /// OAuth token endpoint (default: Google OAuth token endpoint).
    /// </summary>
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// OAuth revoke endpoint (default: Google OAuth revoke endpoint).
    /// </summary>
    public string RevokeEndpoint { get; set; } = "https://oauth2.googleapis.com/revoke";

    /// <summary>
    /// OAuth scopes to request. Default: minimal read-only access.
    /// gmail.readonly: Read Gmail messages (no send/modify)
    /// calendar.events: Create/edit calendar events (not full calendar admin)
    /// </summary>
    public List<string> Scopes { get; set; } = new()
    {
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/calendar.events"
    };
}
