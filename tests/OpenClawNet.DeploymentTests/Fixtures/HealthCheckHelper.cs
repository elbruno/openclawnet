namespace OpenClawNet.DeploymentTests.Fixtures;

/// <summary>
/// Helper for polling health check endpoints with exponential backoff.
/// Used by deployment tests to verify services are ready before running assertions.
/// </summary>
public sealed class HealthCheckHelper
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _maxWaitTime;
    private readonly TimeSpan _initialDelay;

    public HealthCheckHelper(
        HttpClient? httpClient = null,
        TimeSpan? maxWaitTime = null,
        TimeSpan? initialDelay = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _maxWaitTime = maxWaitTime ?? TimeSpan.FromSeconds(60);
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>
    /// Polls a health check endpoint until it returns 200 OK, with exponential backoff.
    /// Throws TimeoutException if the endpoint does not become healthy within maxWaitTime.
    /// </summary>
    public async Task WaitForHealthyAsync(string healthEndpoint, CancellationToken ct = default)
    {
        var elapsed = TimeSpan.Zero;
        var delay = _initialDelay;

        while (elapsed < _maxWaitTime)
        {
            try
            {
                var response = await _httpClient.GetAsync(healthEndpoint, ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Service not yet ready; continue polling
            }

            await Task.Delay(delay, ct);
            elapsed += delay;

            // Exponential backoff: double the delay, capped at 5 seconds
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
        }

        throw new TimeoutException(
            $"Health check endpoint '{healthEndpoint}' did not return 200 OK within {_maxWaitTime.TotalSeconds} seconds");
    }

    /// <summary>
    /// Polls a health check endpoint and parses the response as JSON,
    /// verifying that the "status" field equals "healthy".
    /// </summary>
    public async Task<bool> IsHealthyAsync(string healthEndpoint, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(healthEndpoint, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var status = doc.RootElement.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString()
                : null;

            return status == "healthy";
        }
        catch
        {
            return false;
        }
    }
}
