using System.Text.Json;

namespace OpenClawNet.Gateway.Services;

/// <summary>
/// Describes the full configuration for a model provider at runtime.
/// Serialised to disk so settings survive gateway restarts.
/// </summary>
public sealed class ModelProviderConfig
{
    /// <summary>Active provider: "ollama" | "azure-openai" | "foundry" | "foundry-local"</summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>Model name or deployment name (provider-specific default when null/empty).</summary>
    public string? Model { get; set; }

    /// <summary>Endpoint URL — required for Ollama, Azure OpenAI, and Foundry.</summary>
    public string? Endpoint { get; set; }

    // Azure OpenAI specific
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    /// <summary>"api-key" (default) or "integrated" (DefaultAzureCredential).</summary>
    public string? AuthMode { get; set; }

    /// <summary>
    /// Ordered list of fallback provider names to try when the primary provider fails.
    /// Example: <c>["foundry-local", "ollama"]</c>.
    /// Each fallback entry is resolved using the same provider-switching logic as the primary.
    /// </summary>
    public List<string> Fallbacks { get; set; } = [];

    // === NEW: Microsoft Foundry (cloud) ===
    /// <summary>Foundry project endpoint for cloud model hosting.</summary>
    public string? FoundryProjectEndpoint { get; set; }
    /// <summary>"api-key" or "integrated" (DefaultAzureCredential).</summary>
    public string? FoundryAuthMode { get; set; }

    // === NEW: GitHub Copilot SDK ===
    /// <summary>Whether to enable the GitHub Copilot provider.</summary>
    public bool CopilotEnabled { get; set; }
    /// <summary>Copilot model override (e.g. "gpt-5-mini").</summary>
    public string? CopilotModel { get; set; }

    /// <summary>Stable key used to detect when the client needs to be recreated.</summary>
    internal string CacheKey =>
        $"{Provider}|{Endpoint}|{Model}|{DeploymentName}|{AuthMode}|{(string.IsNullOrEmpty(ApiKey) ? "nokey" : "haskey")}|{FoundryProjectEndpoint}|{FoundryAuthMode}|{CopilotEnabled}|{CopilotModel}";
}

/// <summary>
/// Thread-safe singleton that holds the active <see cref="ModelProviderConfig"/>.
/// Changes are persisted to a local JSON file so they survive gateway restarts.
/// The initial value is bootstrapped from <see cref="IConfiguration"/> (appsettings / user secrets / env),
/// but any subsequent update stored in the JSON file takes precedence on the next start.
/// </summary>
public sealed class RuntimeModelSettings
{
    private volatile ModelProviderConfig _current;
    private readonly string _persistPath;
    private readonly ILogger<RuntimeModelSettings> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RuntimeModelSettings(IConfiguration configuration, IHostEnvironment env, ILogger<RuntimeModelSettings> logger)
    {
        _logger = logger;
        _persistPath = Path.Combine(env.ContentRootPath, "model-settings.json");
        _current = Load(configuration);
        _logger.LogInformation("RuntimeModelSettings initialised: Provider={Provider}, Model={Model}",
            _current.Provider, _current.Model ?? "(default)");
    }

    /// <summary>Current active provider configuration (snapshot).</summary>
    public ModelProviderConfig Current => _current;

    /// <summary>Atomically replaces the active config and persists to disk.</summary>
    public void Update(ModelProviderConfig config)
    {
        Interlocked.Exchange(ref _current, config);
        Persist(config);
        _logger.LogInformation("Model provider changed to {Provider}", config.Provider);
    }

    // ──────────────────────────────────────────────────────────

    private ModelProviderConfig Load(IConfiguration config)
    {
        if (File.Exists(_persistPath))
        {
            try
            {
                var json = File.ReadAllText(_persistPath);
                var loaded = JsonSerializer.Deserialize<ModelProviderConfig>(json);
                if (loaded is not null)
                {
                    _logger.LogDebug("Loaded persisted model settings from {Path}", _persistPath);

                    // IConfiguration (user-secrets / env vars) ALWAYS wins for key fields.
                    // The JSON file is a convenience for remembering last UI settings,
                    // not for overriding explicit config.
                    var configProvider = config["Model:Provider"];
                    if (!string.IsNullOrEmpty(configProvider))
                    {
                        loaded.Provider = configProvider;

                        // When provider is overridden, clear Model to prevent cross-provider
                        // contamination. For azure-openai, the model is DeploymentName, not Model.
                        // The appsettings.json Model field is Ollama-specific ("gemma4:e2b") and
                        // must NOT leak into Azure OpenAI config.
                        if (!configProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                            loaded.Model = null;
                    }

                    // Only apply Model from IConfiguration for Ollama provider
                    // (appsettings.json has "Model": "gemma4:e2b" which is Ollama-specific)
                    if (loaded.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        var configModel = config["Model:Model"];
                        if (!string.IsNullOrEmpty(configModel))
                            loaded.Model = configModel;
                    }

                    var configEndpoint = config["Model:Endpoint"];
                    if (!string.IsNullOrEmpty(configEndpoint))
                        loaded.Endpoint = configEndpoint;

                    var configDeployment = config["Model:DeploymentName"];
                    if (!string.IsNullOrEmpty(configDeployment))
                        loaded.DeploymentName = configDeployment;

                    var configAuthMode = config["Model:AuthMode"];
                    if (!string.IsNullOrEmpty(configAuthMode))
                        loaded.AuthMode = configAuthMode;

                    // Backfill secrets from IConfiguration (user secrets / env vars).
                    if (string.IsNullOrEmpty(loaded.ApiKey))
                        loaded.ApiKey = config["Model:ApiKey"];

                    return loaded;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load persisted model settings from {Path}; using IConfiguration defaults", _persistPath);
            }
        }

        return FromConfiguration(config);
    }

    private static ModelProviderConfig FromConfiguration(IConfiguration config)
    {
        var fallbacks = config.GetSection("Model:Fallbacks")
            .GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var provider = config["Model:Provider"] ?? "ollama";
        var endpoint = config["Model:Endpoint"];

        // When running in Aspire, the Ollama endpoint is provided via service discovery
        // (ConnectionStrings:ollama), not Model:Endpoint. Fall back to the Aspire-assigned
        // URL before defaulting to localhost:11434.
        if (string.IsNullOrEmpty(endpoint) && provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = config.GetConnectionString("ollama")
                    ?? config["services:ollama:http:0"];
        }

        return new()
        {
            Provider       = provider,
            Model          = config["Model:Model"],
            Endpoint       = endpoint,
            ApiKey         = config["Model:ApiKey"],
            DeploymentName = config["Model:DeploymentName"],
            AuthMode       = config["Model:AuthMode"],
            Fallbacks      = fallbacks,
        };
    }

    private void Persist(ModelProviderConfig config)
    {
        try
        {
            // NOTE: For this educational demo, the ApiKey IS persisted to disk so that
            // keys set via the Settings UI survive restarts. Production apps should use
            // Azure Key Vault or dotnet user-secrets instead of plain-text JSON.
            var safe = new ModelProviderConfig
            {
                Provider              = config.Provider,
                Model                 = config.Model,
                Endpoint              = config.Endpoint,
                DeploymentName        = config.DeploymentName,
                AuthMode              = config.AuthMode,
                ApiKey                = config.ApiKey,
                FoundryProjectEndpoint = config.FoundryProjectEndpoint,
                FoundryAuthMode       = config.FoundryAuthMode,
                CopilotEnabled        = config.CopilotEnabled,
                CopilotModel          = config.CopilotModel,
            };
            File.WriteAllText(_persistPath, JsonSerializer.Serialize(safe, JsonOpts));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist model settings");
        }
    }
}
