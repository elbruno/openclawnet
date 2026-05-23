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
        "Convert a web URL (http/https) into clean Markdown by fetching the page and stripping HTML navigation, scripts, and styles. Use this tool when users ask to summarize website content or extract the latest website/blog content — convert first, then summarize from the markdown. Use web_fetch only when raw page content is explicitly requested instead of markdown conversion. Do NOT use this tool for file operations, shell commands, or non-web tasks.";

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
        Tags = ["markdown", "web", "convert", "url"]
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

            string markdown;
            string sourceFormat;
            string resolvedExt = "n/a";
            static bool ShouldFallbackToHttp(ConversionResult r)
            {
                var md = r.Markdown ?? string.Empty;
                return !r.Success ||
                       string.IsNullOrWhiteSpace(md) ||
                       md.Contains("Blocked URL", StringComparison.OrdinalIgnoreCase);
            }
            try
            {
                // Preferred path: let MarkItDotNet fetch + convert directly from URL.
                // This uses the library's URL conversion pipeline intended for website
                // summarization scenarios.
                var result = await _markdown.ConvertUrlAsync(url);

                if (!ShouldFallbackToHttp(result))
                {
                    markdown = result.Markdown!;
                    sourceFormat = result.SourceFormat?.ToString() ?? "unknown";
                }
                else
                {
                    _logger.LogWarning(
                        "ConvertUrlAsync returned unusable content for {Url}. Falling back to HttpClient stream conversion. Success={Success} Error={Error}",
                        url,
                        result.Success,
                        result.ErrorMessage);

                    // Fallback path for compatibility with test infrastructure and
                    // network edge-cases where URL conversion may fail.
                    var http = _httpFactory.CreateClient(nameof(MarkItDownTool));
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        return ToolResult.Fail(Name, $"markdown_convert failed for {url}: HTTP {(int)response.StatusCode} {response.StatusCode}", sw.Elapsed);

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var ext = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);
                    resolvedExt = ext;
                    var fallbackResult = await _markdown.ConvertAsync(stream, ext);

                    if (!fallbackResult.Success)
                        return ToolResult.Fail(Name,
                            $"markdown_convert failed for {url}: MarkItDotNet returned Success=false ({fallbackResult.ErrorMessage ?? "no error message"})",
                            sw.Elapsed);

                    markdown = fallbackResult.Markdown ?? string.Empty;
                    sourceFormat = fallbackResult.SourceFormat?.ToString() ?? "unknown";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ConvertUrlAsync threw for {Url}. Falling back to HttpClient stream conversion.", url);
                try
                {
                    var http = _httpFactory.CreateClient(nameof(MarkItDownTool));
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        return ToolResult.Fail(Name, $"markdown_convert failed for {url}: HTTP {(int)response.StatusCode} {response.StatusCode}", sw.Elapsed);

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var ext = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);
                    resolvedExt = ext;
                    var fallbackResult = await _markdown.ConvertAsync(stream, ext);
                    if (!fallbackResult.Success)
                        return ToolResult.Fail(Name,
                            $"markdown_convert failed for {url}: MarkItDotNet returned Success=false ({fallbackResult.ErrorMessage ?? "no error message"})",
                            sw.Elapsed);

                    markdown = fallbackResult.Markdown ?? string.Empty;
                    sourceFormat = fallbackResult.SourceFormat?.ToString() ?? "unknown";
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "MarkItDotNet conversion failed for {Url}", url);
                    return ToolResult.Fail(
                        Name,
                        $"markdown_convert failed for {url}: MarkItDotNet threw {fallbackEx.GetType().Name}: {fallbackEx.Message}",
                        sw.Elapsed);
                }
            }

            if (string.IsNullOrWhiteSpace(markdown))
                return ToolResult.Fail(Name,
                    $"markdown_convert produced empty output for {url} (sourceFormat={sourceFormat}, ext={resolvedExt}). " +
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
