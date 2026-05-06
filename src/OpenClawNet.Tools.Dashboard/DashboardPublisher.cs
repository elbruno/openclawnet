using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Tools.Dashboard;

/// <summary>
/// Publishes insights to an external dashboard via HTTP POST.
/// </summary>
public sealed class DashboardPublisher : IDashboardPublisher
{
    private static readonly ActivitySource Source = new("OpenClawNet.Tools.Dashboard");
    private static readonly Meter Meter = new("OpenClawNet.Tools.Dashboard");
    private static readonly Counter<long> PublishRequestsCounter = Meter.CreateCounter<long>(
        "dashboard.publish.requests",
        description: "Total dashboard publish requests");
    private static readonly Histogram<double> PublishDurationHistogram = Meter.CreateHistogram<double>(
        "dashboard.publish.duration",
        unit: "ms",
        description: "Duration of dashboard publish operations");

    private readonly HttpClient _httpClient;
    private readonly IOptions<DashboardOptions> _options;
    private readonly ILogger<DashboardPublisher> _logger;

    public DashboardPublisher(
        IHttpClientFactory httpClientFactory,
        IOptions<DashboardOptions> options,
        ILogger<DashboardPublisher> logger)
    {
        _httpClient = httpClientFactory.CreateClient("dashboard");
        _options = options;
        _logger = logger;
    }

    public async Task<DashboardPublishResult> PublishAsync(DashboardPublishRequest request, CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        
        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
            throw new InvalidOperationException("Dashboard BaseUrl is not configured.");
        
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException("Dashboard ApiKey is not configured.");

        var endpoint = $"{opts.BaseUrl.TrimEnd('/')}/api/v1/insights";
        var targetHost = new Uri(opts.BaseUrl).Host;
        var repoCount = request.Insights.Count;
        
        using var activity = Source.StartActivity("dashboard.publish");
        activity?.SetTag("dashboard.target_host", targetHost);
        activity?.SetTag("dashboard.payload.metric_count", repoCount);
        activity?.SetTag("dashboard.payload.repo_count", repoCount);

        var sw = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "Publishing {RepoCount} insights to dashboard at {TargetHost}",
            repoCount,
            targetHost);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
        
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var statusCode = (int)response.StatusCode;
            var statusCodeClass = statusCode switch
            {
                >= 200 and < 300 => "2xx",
                >= 400 and < 500 => "4xx",
                >= 500 => "5xx",
                _ => "other"
            };
            
            activity?.SetTag("http.status_code", statusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var bodyExcerpt = body.Length > 200 ? body[..200] + "..." : body;
                
                activity?.SetTag("dashboard.success", false);
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {statusCode}");
                
                sw.Stop();
                var failureTags = new[]
                {
                    new KeyValuePair<string, object?>("success", false), 
                    new KeyValuePair<string, object?>("status_code_class", statusCodeClass)
                };
                PublishRequestsCounter.Add(1, failureTags);
                PublishDurationHistogram.Record(sw.Elapsed.TotalMilliseconds, 
                    new KeyValuePair<string, object?>("success", false));
                
                if (statusCode >= 500)
                {
                    _logger.LogError(
                        "Dashboard API returned {StatusCode}. Body excerpt: {BodyExcerpt}. Duration: {DurationMs}ms",
                        statusCode,
                        bodyExcerpt,
                        sw.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogWarning(
                        "Dashboard API returned {StatusCode}. Body excerpt: {BodyExcerpt}. Duration: {DurationMs}ms",
                        statusCode,
                        bodyExcerpt,
                        sw.ElapsedMilliseconds);
                }
                
                throw new DashboardPublisherException(response.StatusCode, body);
            }

            if (response.StatusCode != HttpStatusCode.Created)
            {
                _logger.LogWarning(
                    "Dashboard API returned {StatusCode} (expected 201 Created)",
                    response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<DashboardPublishResult>(cancellationToken);
            
            if (result is null)
                throw new InvalidOperationException("Dashboard API returned null response body.");

            activity?.SetTag("dashboard.success", true);
            sw.Stop();
            
            var successTags = new[]
            {
                new KeyValuePair<string, object?>("success", true), 
                new KeyValuePair<string, object?>("status_code_class", statusCodeClass)
            };
            PublishRequestsCounter.Add(1, successTags);
            PublishDurationHistogram.Record(sw.Elapsed.TotalMilliseconds, 
                new KeyValuePair<string, object?>("success", true));

            _logger.LogInformation(
                "Successfully published to dashboard: {ViewUrl}, Duration: {DurationMs}ms, StatusCode: {StatusCode}",
                result.ViewUrl,
                sw.ElapsedMilliseconds,
                statusCode);

            return result;
        }
        catch (Exception ex) when (ex is not DashboardPublisherException)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("dashboard.success", false);
            
            var errorTags = new[]
            {
                new KeyValuePair<string, object?>("success", false), 
                new KeyValuePair<string, object?>("status_code_class", "error")
            };
            PublishRequestsCounter.Add(1, errorTags);
            PublishDurationHistogram.Record(sw.Elapsed.TotalMilliseconds, 
                new KeyValuePair<string, object?>("success", false));
            
            _logger.LogError(ex, 
                "Dashboard publish failed with exception. Duration: {DurationMs}ms",
                sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }
}
