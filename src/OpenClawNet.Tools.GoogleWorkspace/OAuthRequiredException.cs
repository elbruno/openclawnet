namespace OpenClawNet.Tools.GoogleWorkspace;

/// <summary>
/// Exception thrown when a Google API operation requires OAuth authorization but no valid token exists.
/// </summary>
public sealed class OAuthRequiredException : InvalidOperationException
{
    public string UserId { get; }

    public OAuthRequiredException(string userId, string message)
        : base(message)
    {
        UserId = userId;
    }

    public OAuthRequiredException(string userId, string message, Exception innerException)
        : base(message, innerException)
    {
        UserId = userId;
    }
}
