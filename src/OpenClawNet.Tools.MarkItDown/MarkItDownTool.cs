using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ElBruno.MarkItDotNet;
using Microsoft.Extensions.Logging;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.MarkItDown;

/// <summary>
/// Converts a URL (or other supported resource) into clean Markdown using
/// the ElBruno.MarkItDotNet library. Strips navigation/scripts/styles and
/// extracts the page title so agents can reason over readable content
/// instead of raw HTML.
///
/// Optionally persists the result to the OpenClawNet storage directory when
/// the caller passes <c>save_to_file</c>: true. The saved file path is
/// returned as part of the tool output so the agent can reference it.
/// </summary>
public sealed class MarkItDownTool : ITool
{
    private readonly MarkdownService _markdown;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MarkItDownTool> _logger;
    private readonly IToolStorageProvider? _storage;

    public MarkItDownTool(
        MarkdownService markdown,
        IHttpClientFactory httpFactory,
        ILogger<MarkItDownTool> logger,
        IToolStorageProvider? storage = null)
    {
        _markdown = markdown;
        _httpFactory = httpFactory;
        _logger = logger;
        _storage = storage;
    }

    public string Name => "markdown_convert";

    public string Description =>
        "Convert a web URL (http/https) into clean Markdown by fetching the page and stripping HTML navigation, scripts, and styles. " +
        "ONLY use this tool when the user explicitly asks to convert a URL to Markdown, summarize a web page, or download web content as Markdown. " +
        "Do NOT use this tool for file operations, shell commands, or non-web tasks. " +
        "Optionally save the result to file by passing save_to_file=true.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "url": { "type": "string", "description": "Absolute http/https URL to fetch and convert to Markdown" },
                "save_to_file": { "type": "boolean", "description": "When true, write the markdown to OpenClawNet storage and return the file path. Defaults to false." },
                "filename": { "type": "string", "description": "Optional filename (without directory). Defaults to a slug derived from the URL host plus a timestamp. The .md extension is added automatically." }
            },
            "required": ["url"]
        }
        """),
        RequiresApproval = false,
        Category = "web",
        Tags = ["markdown", "web", "convert", "url", "summarize"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var url = input.GetStringArgument("url");
            if (string.IsNullOrWhiteSpace(url))
                return ToolResult.Fail(Name, "'url' is required", sw.Elapsed);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return ToolResult.Fail(Name, $"Invalid URL: {url}. Only http and https are supported.", sw.Elapsed);

            if (IsLocalUri(uri))
                return ToolResult.Fail(Name, "Fetching from local/private addresses is not allowed", sw.Elapsed);

            _logger.LogInformation("Converting URL to Markdown: {Url}", url);

            // Fetch with our managed HttpClient so timeouts/proxies/HSTS apply uniformly,
            // then hand the stream to MarkdownService which auto-detects HTML.
            var http = _httpFactory.CreateClient(nameof(MarkItDownTool));
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ToolResult.Fail(Name, $"HTTP {(int)response.StatusCode} {response.StatusCode} when fetching {url}", sw.Elapsed);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var ext = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);
            var result = await _markdown.ConvertAsync(stream, ext);
            sw.Stop();

            if (!result.Success)
                return ToolResult.Fail(Name, $"MarkItDotNet failed: {result.ErrorMessage ?? "unknown error"}", sw.Elapsed);

            var markdown = $"# Source: {url}\n# Format: {result.SourceFormat}\n\n{result.Markdown}";

            // Optional persistence
            var saveToFile = input.GetBoolArgument("save_to_file");
            if (saveToFile)
            {
                if (_storage is null)
                {
                    _logger.LogWarning("save_to_file=true was requested but no IToolStorageProvider is registered.");
                    return ToolResult.Ok(Name,
                        markdown + "\n\n⚠️ Storage provider unavailable — markdown was NOT persisted to disk.",
                        sw.Elapsed);
                }

                try
                {
                    var dir = _storage.GetToolStorageDirectory(Name);
                    var requestedName = input.GetStringArgument("filename");
                    var fileName = BuildFileName(requestedName, uri);
                    var fullPath = Path.Combine(dir, fileName);
                    await File.WriteAllTextAsync(fullPath, markdown, Encoding.UTF8, cancellationToken);
                    _logger.LogInformation("Saved markdown ({Bytes} bytes) to {Path}", markdown.Length, fullPath);

                    var summary =
                        $"# Source: {url}\n# Format: {result.SourceFormat}\n# Saved: {fullPath}\n# Bytes: {markdown.Length}\n\n" +
                        $"Markdown saved successfully to `{fullPath}`.\n\n" +
                        $"--- Preview (first 500 chars) ---\n{Truncate(result.Markdown, 500)}";
                    return ToolResult.Ok(Name, summary, sw.Elapsed);
                }
                catch (Exception ioEx)
                {
                    _logger.LogError(ioEx, "Failed to persist markdown to storage");
                    return ToolResult.Ok(Name,
                        markdown + $"\n\n⚠️ Failed to save to disk: {ioEx.Message}",
                        sw.Elapsed);
                }
            }

            return ToolResult.Ok(Name, markdown, sw.Elapsed);
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail(Name, "Request timed out", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkItDown tool error");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
    }

    private static string BuildFileName(string? requested, Uri uri)
    {
        string baseName;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            baseName = SanitizeForFilesystem(Path.GetFileNameWithoutExtension(requested))
                       ?? "markdown";
        }
        else
        {
            var hostSlug = SanitizeForFilesystem(uri.Host) ?? "page";
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            baseName = $"{hostSlug}-{timestamp}";
        }
        return baseName + ".md";
    }

    private static string? SanitizeForFilesystem(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (Array.IndexOf(invalid, ch) >= 0 || ch == ' ') sb.Append('-');
            else sb.Append(ch);
        }
        return sb.ToString().Trim('-', '.');
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static string ResolveExtension(string? mediaType, Uri uri)
    {
        // Prefer Content-Type. Fall back to URL path extension. Default to .html.
        if (!string.IsNullOrEmpty(mediaType))
        {
            var mt = mediaType.ToLowerInvariant();
            if (mt.Contains("html")) return ".html";
            if (mt.Contains("pdf")) return ".pdf";
            if (mt.Contains("plain")) return ".txt";
            if (mt.Contains("markdown")) return ".md";
            if (mt.Contains("csv")) return ".csv";
            if (mt.Contains("json")) return ".json";
            if (mt.Contains("xml")) return ".xml";
        }
        var pathExt = Path.GetExtension(uri.AbsolutePath);
        return string.IsNullOrEmpty(pathExt) ? ".html" : pathExt.ToLowerInvariant();
    }

    private static bool IsLocalUri(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host == "localhost" ||
               host == "127.0.0.1" ||
               host == "::1" ||
               host.StartsWith("192.168.") ||
               host.StartsWith("10.") ||
               host.StartsWith("172.16.");
    }
}
