using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;
using OpenClawNet.Models.Ollama;

namespace OpenClawNet.UnitTests.Integration;

/// <summary>
/// Shared fixture for live LLM integration tests.
///
/// Usage:
///   public sealed class MyLiveTests : IClassFixture&lt;LiveTestFixture&gt;
///   {
///       private readonly LiveTestFixture _fx;
///       public MyLiveTests(LiveTestFixture fx) => _fx = fx;
///
///       [SkippableFact]
///       [Trait("Category", "Live")]
///       public async Task Foo()
///       {
///           await LiveTestFixture.SkipIfOllamaUnavailableAsync(_fx.OllamaClient);
///           // ...use _fx.OllamaClient...
///       }
///   }
///
/// Or parameterize across providers via <see cref="BothProviders"/>:
///   [SkippableTheory]
///   [MemberData(nameof(LiveTestFixture.BothProviders), MemberType = typeof(LiveTestFixture))]
///   public async Task Foo(string providerName, Func&lt;LiveTestFixture, IModelClient?&gt; pick) { ... }
///
/// Configuration:
///   - Ollama: endpoint defaults to http://localhost:11434. Override via env LIVE_TEST_OLLAMA_ENDPOINT.
///     Model defaults to "qwen2.5:3b". Override via env LIVE_TEST_OLLAMA_MODEL.
///   - Azure OpenAI: read from Gateway user-secrets (Model:Endpoint, Model:ApiKey,
///     Model:DeploymentName, Model:AuthMode). When unset, AzureClient is null and
///     SkipIfAzureUnavailable() will skip the test.
///   - Default per-call timeout: Ollama 30s, Azure 60s. Override via env
///     LIVE_TEST_TIMEOUT_SECONDS (applies to both).
/// </summary>
public sealed class LiveTestFixture : IDisposable
{
    public const int OllamaTimeoutSeconds = 120;
    public const int AzureTimeoutSeconds = 60;

    // Same UserSecretsId as the Gateway project — keeps live tests in sync with prod config.
    private const string GatewayUserSecretsId = "c15754a6-dc90-4a2a-aecb-1233d1a54fe1";
    private const string DefaultOllamaEndpoint = "http://localhost:11434";
    private const string DefaultOllamaModel = "qwen2.5:3b";

    private readonly HttpClient _ollamaHttp;

    public OllamaModelClient OllamaClient { get; }
    public AzureOpenAIModelClient? AzureClient { get; }
    public bool IsAzureConfigured { get; }

    public string OllamaEndpoint { get; }
    public string OllamaModel { get; }

    /// <summary>Effective per-call timeout for Ollama tests.</summary>
    public TimeSpan OllamaTimeout { get; }

    /// <summary>Effective per-call timeout for Azure OpenAI tests.</summary>
    public TimeSpan AzureTimeout { get; }

    public LiveTestFixture()
    {
        OllamaEndpoint = Environment.GetEnvironmentVariable("LIVE_TEST_OLLAMA_ENDPOINT") is { Length: > 0 } e
            ? e
            : DefaultOllamaEndpoint;
        OllamaModel = Environment.GetEnvironmentVariable("LIVE_TEST_OLLAMA_MODEL") is { Length: > 0 } m
            ? m
            : DefaultOllamaModel;

        var overrideSecs = Environment.GetEnvironmentVariable("LIVE_TEST_TIMEOUT_SECONDS");
        OllamaTimeout = TimeSpan.FromSeconds(
            int.TryParse(overrideSecs, out var s) && s > 0 ? s : OllamaTimeoutSeconds);
        AzureTimeout = TimeSpan.FromSeconds(
            int.TryParse(overrideSecs, out var s2) && s2 > 0 ? s2 : AzureTimeoutSeconds);

        _ollamaHttp = new HttpClient { BaseAddress = new Uri(OllamaEndpoint), Timeout = OllamaTimeout };
        var ollamaOptions = Options.Create(new OllamaOptions
        {
            Endpoint = OllamaEndpoint,
            Model = OllamaModel,
            Temperature = 0.0,
            MaxTokens = 512
        });
        OllamaClient = new OllamaModelClient(_ollamaHttp, ollamaOptions, NullLogger<OllamaModelClient>.Instance);

        var (azureClient, configured) = TryBuildAzureClient();
        AzureClient = azureClient;
        IsAzureConfigured = configured;
    }

    private static (AzureOpenAIModelClient? Client, bool IsConfigured) TryBuildAzureClient()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(GatewayUserSecretsId, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var opts = new AzureOpenAIOptions();
        if (config["Model:Endpoint"] is { Length: > 0 } ep) opts.Endpoint = ep;
        if (config["Model:ApiKey"] is { Length: > 0 } key) opts.ApiKey = key;
        if (config["Model:DeploymentName"] is { Length: > 0 } dep) opts.DeploymentName = dep;
        if (config["Model:AuthMode"] is { Length: > 0 } mode) opts.AuthMode = mode;

        var configured = !string.IsNullOrEmpty(opts.Endpoint)
            && (string.Equals(opts.AuthMode, "integrated", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(opts.ApiKey));

        if (!configured) return (null, false);

        var client = new AzureOpenAIModelClient(
            Options.Create(opts),
            NullLogger<AzureOpenAIModelClient>.Instance);
        return (client, true);
    }

    // ── Skip helpers ───────────────────────────────────────────────────────

    public static async Task SkipIfOllamaUnavailableAsync(IModelClient client)
    {
        var available = false;
        try { available = await client.IsAvailableAsync(); }
        catch { available = false; }
        Skip.IfNot(available, "Ollama is not running at the configured endpoint (set LIVE_TEST_OLLAMA_ENDPOINT to override).");
    }

    public static void SkipIfAzureUnavailable(AzureOpenAIModelClient? client)
    {
        Skip.If(client is null, "Azure OpenAI credentials not configured — set Gateway user secrets (Model:Endpoint, Model:ApiKey, Model:DeploymentName) to run.");
    }

    /// <summary>
    /// Synchronous variant of <see cref="SkipIfOllamaUnavailableAsync"/> for use inside
    /// <c>MemberData</c>-driven theories where async isn't ergonomic.
    /// </summary>
    public static void SkipIfOllamaUnavailable(IModelClient client)
        => SkipIfOllamaUnavailableAsync(client).GetAwaiter().GetResult();

    // ── Provider parameterization ──────────────────────────────────────────

    /// <summary>
    /// Yields (providerName, picker) tuples so tests can run against both Ollama and AOAI.
    /// The picker takes the fixture and returns the corresponding IModelClient
    /// (or null when the provider is not configured — the test should skip).
    /// </summary>
    public static IEnumerable<object[]> BothProviders()
    {
        yield return new object[] { "ollama",       (Func<LiveTestFixture, IModelClient?>)(fx => fx.OllamaClient) };
        yield return new object[] { "azure-openai", (Func<LiveTestFixture, IModelClient?>)(fx => fx.AzureClient) };
    }

    public void Dispose()
    {
        _ollamaHttp.Dispose();
    }
}
