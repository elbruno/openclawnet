using System.Net;

namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Exception thrown when the dashboard API returns a non-2xx status code.
/// </summary>
public sealed class DashboardPublisherException : Exception
{
    /// <summary>
    /// HTTP status code returned by the dashboard API.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Excerpt of the response body from the dashboard API.
    /// </summary>
    public string? ResponseBody { get; }

    public DashboardPublisherException(HttpStatusCode statusCode, string? responseBody)
        : base($"Dashboard API returned {(int)statusCode} {statusCode}. Body excerpt: {Truncate(responseBody, 200)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "(empty)";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
