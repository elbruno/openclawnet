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
    private readonly IMarkdownService _markdown;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IStorageDirectoryProvider _storageProvider;
    private readonly ILogger<MarkItDownTool> _logger;

    public MarkItDownTool(
        IMarkdownService markdown,
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
        
        _logger.LogInformation("=== MarkItDown.ExecuteAsync ENTRY ===");
        _logger.LogInformation("URL={Url}, SaveToFile={SaveToFile}", url, input.GetArgument<bool?>("save_to_file") ?? false);
        
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("URL parameter is null/empty");
                return ToolResult.Fail(Name, "'url' is required", sw.Elapsed);
            }

            _logger.LogInformation("URL validation: attempting to parse {Url}", url);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.LogWarning("URL validation failed: {Url} is not a valid http/https URL", url);
                return ToolResult.Fail(Name, $"Invalid URL: {url}. Only http and https are supported.", sw.Elapsed);
            }

            _logger.LogInformation("URI parsed successfully: Scheme={Scheme}, Host={Host}, Path={Path}", uri.Scheme, uri.Host, uri.AbsolutePath);

            if (IsLocalUri(uri))
            {
                _logger.LogWarning("Local URI check failed: {Url} is a local/private address", url);
                return ToolResult.Fail(Name, $"markdown_convert refused {url}: fetching from local/private addresses is not allowed", sw.Elapsed);
            }

            _logger.LogInformation("Security checks passed, starting conversion for {Url}", url);

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
                _logger.LogInformation("Attempting ConvertUrlAsync (preferred path) for {Url}", url);
                var result = await _markdown.ConvertUrlAsync(url);
                
                _logger.LogInformation("ConvertUrlAsync completed for {Url}: Success={Success}, ContentLength={ContentLength}, ErrorMessage={ErrorMessage}",
                    url, 
                    result.Success,
                    result.Markdown?.Length ?? 0,
                    result.ErrorMessage ?? "(none)");

                _logger.LogInformation(
                    "ConvertUrlAsync completed: Success={Success}, MarkdownLength={MarkdownLength}, SourceFormat={SourceFormat}, ErrorMessage={ErrorMessage}",
                    result.Success,
                    result.Markdown?.Length ?? 0,
                    result.SourceFormat?.ToString() ?? "null",
                    result.ErrorMessage ?? "none");

                if (!ShouldFallbackToHttp(result))
                {
                    _logger.LogInformation("ConvertUrlAsync produced valid markdown ({Length} chars) for {Url}", result.Markdown?.Length ?? 0, url);
                    markdown = result.Markdown!;
                    sourceFormat = result.SourceFormat?.ToString() ?? "unknown";
                }
                else
                {
                    _logger.LogWarning(
                        "ConvertUrlAsync returned unusable content for {Url}. Falling back to HttpClient stream conversion. Success={Success}, MarkdownLength={MarkdownLength}, Error={Error}",
                        url,
                        result.Success,
                        result.Markdown?.Length ?? 0,
                        result.ErrorMessage ?? "(none)");

                    // Fallback path: use HttpClient to fetch as stream
                    _logger.LogInformation("Fallback: Creating HttpClient for {Url}", url);
                    var http = _httpFactory.CreateClient(nameof(MarkItDownTool));
                    
                    _logger.LogInformation("Fallback: Sending GET request to {Url}", url);
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    
                    _logger.LogInformation("Fallback: HTTP response received for {Url}: StatusCode={StatusCode}, ContentType={ContentType}, ContentLength={ContentLength}",
                        url,
                        (int)response.StatusCode,
                        response.Content.Headers.ContentType?.MediaType ?? "(none)",
                        response.Content.Headers.ContentLength ?? 0);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Fallback: HTTP request failed for {Url}: {StatusCode} {ReasonPhrase}", url, (int)response.StatusCode, response.ReasonPhrase);
                        return ToolResult.Fail(Name, $"markdown_convert failed for {url}: HTTP {(int)response.StatusCode} {response.StatusCode}", sw.Elapsed);
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var ext = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);
                    resolvedExt = ext;
                    
                    _logger.LogInformation("Fallback: Stream read, detected extension={Ext}, calling ConvertAsync", ext);
                    var fallbackResult = await _markdown.ConvertAsync(stream, ext);
                    
                    _logger.LogInformation("Fallback: ConvertAsync completed for {Url}: Success={Success}, ContentLength={ContentLength}, ErrorMessage={ErrorMessage}",
                        url,
                        fallbackResult.Success,
                        fallbackResult.Markdown?.Length ?? 0,
                        fallbackResult.ErrorMessage ?? "(none)");

                    _logger.LogInformation(
                        "HTTP fallback ConvertAsync completed: Success={Success}, MarkdownLength={MarkdownLength}, SourceFormat={SourceFormat}, Ext={Ext}",
                        fallbackResult.Success,
                        fallbackResult.Markdown?.Length ?? 0,
                        fallbackResult.SourceFormat?.ToString() ?? "null",
                        ext);

                    if (!fallbackResult.Success)
                    {
                        _logger.LogError("Fallback: MarkItDotNet conversion failed for {Url}: Success=false, Error={Error}",
                            url,
                            fallbackResult.ErrorMessage ?? "(no error message)");
                        return ToolResult.Fail(Name,
                            $"markdown_convert failed for {url}: MarkItDotNet returned Success=false ({fallbackResult.ErrorMessage ?? "no error message"})",
                            sw.Elapsed);
                    }

                    markdown = fallbackResult.Markdown ?? string.Empty;
                    sourceFormat = fallbackResult.SourceFormat?.ToString() ?? "unknown";
                    
                    _logger.LogInformation("Fallback: Conversion succeeded with {Length} chars", markdown.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConvertUrlAsync threw exception for {Url}: {ExceptionType} {Message}. Attempting second fallback with HttpClient stream conversion.", 
                    url, ex.GetType().Name, ex.Message);
                
                try
                {
                    _logger.LogInformation("Second fallback: Creating HttpClient for {Url}", url);
                    var http = _httpFactory.CreateClient(nameof(MarkItDownTool));
                    
                    _logger.LogInformation("Second fallback: Sending GET request to {Url}", url);
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    
                    _logger.LogInformation("Second fallback: HTTP response received for {Url}: StatusCode={StatusCode}, ContentType={ContentType}, ContentLength={ContentLength}",
                        url,
                        (int)response.StatusCode,
                        response.Content.Headers.ContentType?.MediaType ?? "(none)",
                        response.Content.Headers.ContentLength ?? 0);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Second fallback: HTTP request failed for {Url}: {StatusCode} {ReasonPhrase}", url, (int)response.StatusCode, response.ReasonPhrase);
                        return ToolResult.Fail(Name, $"markdown_convert failed for {url}: HTTP {(int)response.StatusCode} {response.StatusCode}", sw.Elapsed);
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var ext = ResolveExtension(response.Content.Headers.ContentType?.MediaType, uri);
                    resolvedExt = ext;
                    
                    _logger.LogInformation("Second fallback: Stream read, detected extension={Ext}, calling ConvertAsync", ext);
                    var fallbackResult = await _markdown.ConvertAsync(stream, ext);
                    
                    _logger.LogInformation("Second fallback: ConvertAsync completed for {Url}: Success={Success}, ContentLength={ContentLength}, ErrorMessage={ErrorMessage}",
                        url,
                        fallbackResult.Success,
                        fallbackResult.Markdown?.Length ?? 0,
                        fallbackResult.ErrorMessage ?? "(none)");
                    
                    if (!fallbackResult.Success)
                    {
                        _logger.LogError("Second fallback: MarkItDotNet conversion failed for {Url}: Success=false, Error={Error}",
                            url,
                            fallbackResult.ErrorMessage ?? "(no error message)");
                        return ToolResult.Fail(Name,
                            $"markdown_convert failed for {url}: MarkItDotNet returned Success=false ({fallbackResult.ErrorMessage ?? "no error message"})",
                            sw.Elapsed);
                    }

                    markdown = fallbackResult.Markdown ?? string.Empty;
                    sourceFormat = fallbackResult.SourceFormat?.ToString() ?? "unknown";
                    
                    _logger.LogInformation("Second fallback: Conversion succeeded with {Length} chars", markdown.Length);
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Second fallback MarkItDotNet conversion failed for {Url}: {ExceptionType} {Message} (Stack: {StackTrace})", 
                        url, 
                        fallbackEx.GetType().Name, 
                        fallbackEx.Message,
                        fallbackEx.StackTrace);
                    return ToolResult.Fail(
                        Name,
                        $"markdown_convert failed for {url}: MarkItDotNet threw {fallbackEx.GetType().Name}: {fallbackEx.Message}",
                        sw.Elapsed);
                }
            }

            _logger.LogInformation("Markdown content check for {Url}: Length={Length}", url, markdown?.Length ?? 0);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                _logger.LogError("Markdown output is empty/null for {Url} (sourceFormat={SourceFormat}, ext={Extension})",
                    url,
                    sourceFormat,
                    resolvedExt);
                return ToolResult.Fail(Name,
                    $"markdown_convert produced empty output for {url} (sourceFormat={sourceFormat}, ext={resolvedExt}). " +
                    "The page may be empty, blocked by a paywall/JS gate, or in an unsupported format.",
                    sw.Elapsed);
            }

            var output = $"# Source: {url}\n# Format: {sourceFormat}\n\n{markdown}";
            _logger.LogInformation("Generated output for {Url}: {OutputLength} chars", url, output.Length);

            // Log the full output for debugging
            var preview = output.Length > 300 ? output.Substring(0, 300) + "..." : output;
            _logger.LogInformation(
                "markdown_convert returning {OutputLength} chars: Success (format={SourceFormat}). Preview:\n{Preview}",
                output.Length, sourceFormat, preview);

            // Log that we're about to return Ok
            _logger.LogInformation("Returning ToolResult.Ok with {OutputLength} chars of content", output.Length);

            // Check if we should save to file
            var saveToFile = input.GetArgument<bool?>("save_to_file") ?? false;
            if (saveToFile)
            {
                _logger.LogInformation("save_to_file requested for {Url}", url);
                var agentName = input.GetStringArgument("agent_name");
                if (string.IsNullOrWhiteSpace(agentName))
                {
                    _logger.LogWarning("save_to_file=true but agent_name not provided for {Url}", url);
                    return ToolResult.Fail(Name, "agent_name is required when save_to_file is true", sw.Elapsed);
                }

                try
                {
                    var storagePath = _storageProvider.GetStorageDirectory(agentName);
                    var filename = GenerateFilenameFromUrl(uri) + ".md";
                    var fullPath = Path.Combine(storagePath, filename);

                    _logger.LogInformation("Writing markdown to file: {Path}", fullPath);
                    await File.WriteAllTextAsync(fullPath, output, cancellationToken);
                    _logger.LogInformation("Successfully saved markdown to {Path}", fullPath);

                    return ToolResult.Ok(Name, $"Markdown saved to: {fullPath}\n\nPreview:\n{output.Substring(0, Math.Min(500, output.Length))}...", sw.Elapsed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save markdown to file for {Url}: {ExceptionType} {Message}", url, ex.GetType().Name, ex.Message);
                    return ToolResult.Fail(Name, $"Failed to save markdown: {ex.Message}", sw.Elapsed);
                }
            }

            _logger.LogInformation("=== MarkItDown.ExecuteAsync EXIT (SUCCESS) === Elapsed={ElapsedMs}ms, OutputLength={OutputLength}", sw.ElapsedMilliseconds, output.Length);
            return ToolResult.Ok(Name, output, sw.Elapsed);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "=== MarkItDown.ExecuteAsync TIMEOUT === URL={Url}, Elapsed={ElapsedMs}ms", url, sw.ElapsedMilliseconds);
            return ToolResult.Fail(Name, $"markdown_convert timed out fetching {url}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== MarkItDown.ExecuteAsync EXCEPTION === URL={Url}, ExceptionType={ExceptionType}, Message={Message}, StackTrace={StackTrace}, Elapsed={ElapsedMs}ms", 
                url, 
                ex.GetType().Name, 
                ex.Message,
                ex.StackTrace,
                sw.ElapsedMilliseconds);
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
