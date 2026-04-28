namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Health check service for Ollama availability.
/// Queries the Ollama API to verify it's running and responsive.
/// </summary>
public class OllamaHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaHealthCheck> _logger;

    public OllamaHealthCheck(HttpClient httpClient, ILogger<OllamaHealthCheck> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Checks Ollama health by querying the /api/tags endpoint.
    /// Returns True if Ollama is available, False otherwise.
    /// </summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("http://localhost:11434/api/tags", HttpCompletionOption.ResponseHeadersRead);
            var isHealthy = response.IsSuccessStatusCode;
            
            if (!isHealthy)
                _logger.LogWarning("Ollama health check failed with status {StatusCode}", response.StatusCode);
            
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama health check failed with exception");
            return false;
        }
    }
}
