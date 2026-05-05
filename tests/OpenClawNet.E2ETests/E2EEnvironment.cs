using Microsoft.Extensions.Configuration;

namespace OpenClawNet.E2ETests;

/// <summary>
/// Centralised env-var detection for the E2E live tests.
/// <para>
/// Tests skip cleanly when the required Azure OpenAI knobs are missing, so this
/// class never throws at startup. The accepted env vars (in priority order) are:
/// </para>
/// <list type="bullet">
///   <item><c>AZURE_OPENAI_ENDPOINT</c> — required (e.g. <c>https://my.openai.azure.com/</c>).</item>
///   <item>One of:
///     <list type="bullet">
///       <item><c>AZURE_OPENAI_KEY</c> or <c>AZURE_OPENAI_API_KEY</c> — API-key auth, OR</item>
///       <item><c>AZURE_OPENAI_AUTH_MODE=integrated</c> — DefaultAzureCredential / managed identity.</item>
///     </list>
///   </item>
///   <item><c>AZURE_OPENAI_DEPLOYMENT</c> or <c>AZURE_OPENAI_DEPLOYMENT_NAME</c> — required.</item>
/// </list>
/// <para>
/// As a fallback for local dev parity with the existing unit-test live suite,
/// the gateway's <c>Model:*</c> user secrets are also accepted.
/// </para>
/// </summary>
internal static class E2EEnvironment
{
    // Matches OpenClawNet.UnitTests AzureOpenAILiveTests / LiveLlmTests.
    public const string GatewayUserSecretsId = "c15754a6-dc90-4a2a-aecb-1233d1a54fe1";

    public static (string? Endpoint, string? ApiKey, string? Deployment, string? AuthMode) ReadAzureOpenAi()
    {
        var endpoint = Env("AZURE_OPENAI_ENDPOINT");
        var apiKey = Env("AZURE_OPENAI_KEY") ?? Env("AZURE_OPENAI_API_KEY");
        var deployment = Env("AZURE_OPENAI_DEPLOYMENT") ?? Env("AZURE_OPENAI_DEPLOYMENT_NAME");
        var authMode = Env("AZURE_OPENAI_AUTH_MODE");

        if (endpoint is null || (apiKey is null && !string.Equals(authMode, "integrated", StringComparison.OrdinalIgnoreCase)))
        {
            // Try gateway user secrets as a developer convenience.
            var cfg = new ConfigurationBuilder()
                .AddUserSecrets(GatewayUserSecretsId, reloadOnChange: false)
                .Build();
            endpoint ??= NullIfEmpty(cfg["Model:Endpoint"]);
            apiKey ??= NullIfEmpty(cfg["Model:ApiKey"]);
            deployment ??= NullIfEmpty(cfg["Model:DeploymentName"]);
            authMode ??= NullIfEmpty(cfg["Model:AuthMode"]);
        }

        return (endpoint, apiKey, deployment, authMode);
    }

    public static bool HasAzureOpenAi
    {
        get
        {
            var (ep, key, dep, mode) = ReadAzureOpenAi();
            if (string.IsNullOrWhiteSpace(ep)) return false;
            if (string.IsNullOrWhiteSpace(dep)) return false;
            var integrated = !string.IsNullOrEmpty(mode)
                && mode!.Equals("integrated", StringComparison.OrdinalIgnoreCase);
            return integrated || !string.IsNullOrWhiteSpace(key);
        }
    }

    public const string SkipReason =
        "Azure OpenAI credentials not configured. " +
        "Set AZURE_OPENAI_ENDPOINT + AZURE_OPENAI_DEPLOYMENT + (AZURE_OPENAI_KEY or AZURE_OPENAI_AUTH_MODE=integrated). " +
        "See tests/OpenClawNet.E2ETests/README.md.";

    private static string? Env(string name)
        => NullIfEmpty(Environment.GetEnvironmentVariable(name));

    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
}
