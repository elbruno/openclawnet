using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElBruno.MarkItDotNet;
using Microsoft.Extensions.Logging;
using OpenClawNet.Storage.Services;
using OpenClawNet.Tools.Abstractions;

namespace OpenClawNet.Tools.MarkItDown;

/// <summary>
/// Converts a URL (or other supported resource) into clean Markdown using
/// the ElBruno.MarkItDotNet library. Strips navigation/scripts/styles and
/// extracts the page title so agents can reason over readable content
/// instead of raw HTML.
/// </summary>
public sealed class MarkItDownTool : ITool
{
    private readonly MarkdownService _markdown;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IStorageDirectoryProvider _storageProvider;
    private readonly ILogger<MarkItDownTool> _logger;

    public MarkItDownTool(
        MarkdownService markdown,
        IHttpClientFactory httpFactory,
        IStorageDirectoryProvider storageProvider,
        ILogger<MarkItDownTool> logger)
    {
        _markdown = markdown;
        _httpFactory = httpFactory;
        _storageProvider = storageProvider;
        _logger = logger;
    }

    public string Name => "markdown_convert";

    public string Description =>
        "Convert a web URL (http/https) into clean Markdown by fetching the page and stripping HTML navigation, scripts, and styles. ONLY use this tool when the user explicitly asks to convert a URL to Markdown, summarize a web page, or download web content as Markdown. Do NOT use this tool for file operations, shell commands, or non-web tasks.";

    public ToolMetadata Metadata => new()
    {
        Name = Name,
        Description = Description,
        ParameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "url": { "type": "string", "description": "Absolute http/https URL to fetch and convert to Markdown" },
                "save_to_file": { "type": "boolean", "description": "If true, save the markdown output to a file in the storage directory", "default": false },
                "agent_name": { "type": "string", "description": "Agent name for storage path (required if save_to_file is true)" }
            },
            "required": ["url"]
        }
        """),
        RequiresApproval = true, // Network egress to arbitrary URLs — same risk class as web_fetch
        Category = "web",
        Tags = ["markdown", "web", "convert", "url", "summarize"]
    };

    public async Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var url = input.GetStringArgument("url");
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return ToolResult.Fail(Name, "'url' is required", sw.Elapsed);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return ToolResult.Fail(Name, $"Invalid URL: {url}. Only http and https are supported.", sw.Elapsed);

            if (IsLocalUri(uri))
                return ToolResult.Fail(Name, $"markdown_convert refused {url}: fetching from local/private addresses is not allowed", sw.Elapsed);

            _logger.LogInformation("Converting URL to Markdown: {Url}", url);

            // Fetch with our managed HttpClient so timeouts/proxies/HSTS apply uniformly,
            // then hand the stream to MarkdownService which auto-detects HTML.
            var http = _httpFactory.CreateClient(nameof(MarkItDownTool));
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ToolResult.Fail(Name, $"markdown_convert failed for {url}: HTTP {(int)response.StatusCode} {response.StatusCode}", sw.Elapsed);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var ext = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);

            string markdown;
            string sourceFormat;
            try
            {
                var result = await _markdown.ConvertAsync(stream, ext);
                sw.Stop();

                if (!result.Success)
                    return ToolResult.Fail(Name,
                        $"markdown_convert failed for {url}: MarkItDotNet returned Success=false ({result.ErrorMessage ?? "no error message"})",
                        sw.Elapsed);

                markdown = result.Markdown ?? string.Empty;
                sourceFormat = result.SourceFormat?.ToString() ?? "unknown";
            }
            catch (Exception ex)
            {
                // ElBruno.MarkItDotNet has historically thrown on a few edge cases
                // (encoding mismatches, malformed HTML, missing native deps).
                // Surface the FULL exception type + message so the failure card on
                // the channel detail page has something actionable instead of an
                // opaque "tool failed" string.
                _logger.LogError(ex, "MarkItDotNet.ConvertAsync threw for {Url} (ext={Ext})", url, ext);
                return ToolResult.Fail(
                    Name,
                    $"markdown_convert failed for {url}: MarkItDotNet threw {ex.GetType().Name}: {ex.Message}",
                    sw.Elapsed);
            }

            if (string.IsNullOrWhiteSpace(markdown))
                return ToolResult.Fail(Name,
                    $"markdown_convert produced empty output for {url} (sourceFormat={sourceFormat}, ext={ext}). " +
                    "The page may be empty, blocked by a paywall/JS gate, or in an unsupported format.",
                    sw.Elapsed);

            var output = $"# Source: {url}\n# Format: {sourceFormat}\n\n{markdown}";

            // Check if we should save to file
            var saveToFile = input.GetArgument<bool?>("save_to_file") ?? false;
            if (saveToFile)
            {
                var agentName = input.GetStringArgument("agent_name");
                if (string.IsNullOrWhiteSpace(agentName))
                    return ToolResult.Fail(Name, "agent_name is required when save_to_file is true", sw.Elapsed);

                try
                {
                    var storagePath = _storageProvider.GetStorageDirectory(agentName);
                    var filename = GenerateFilenameFromUrl(uri) + ".md";
                    var fullPath = Path.Combine(storagePath, filename);

                    await File.WriteAllTextAsync(fullPath, output, cancellationToken);
                    _logger.LogInformation("Saved markdown to {Path}", fullPath);

                    return ToolResult.Ok(Name, $"Markdown saved to: {fullPath}\n\nPreview:\n{output.Substring(0, Math.Min(500, output.Length))}...", sw.Elapsed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save markdown to file for {Url}", url);
                    return ToolResult.Fail(Name, $"Failed to save markdown: {ex.Message}", sw.Elapsed);
                }
            }

            return ToolResult.Ok(Name, output, sw.Elapsed);
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail(Name, $"markdown_convert timed out fetching {url}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkItDown tool error for {Url}", url);
            return ToolResult.Fail(Name,
                $"markdown_convert failed for {url}: {ex.GetType().Name}: {ex.Message}",
                sw.Elapsed);
        }
    }

    private static string GenerateFilenameFromUrl(Uri uri)
    {
        // Create a safe filename from URL domain and path
        var host = uri.Host.Replace("www.", "");
        var path = uri.AbsolutePath.Trim('/');
        
        // Combine host and path, sanitize for filesystem
        var combined = string.IsNullOrEmpty(path) ? host : $"{host}-{path}";
        
        // Replace invalid filename characters with hyphens
        var sanitized = Regex.Replace(combined, @"[^\w\-\.]", "-");
        
        // Remove consecutive hyphens and trim
        sanitized = Regex.Replace(sanitized, @"-+", "-").Trim('-');
        
        // Limit length to 200 characters
        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200).TrimEnd('-');
        
        return sanitized;
    }

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
