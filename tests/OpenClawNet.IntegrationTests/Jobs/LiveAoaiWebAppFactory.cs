using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.AzureOpenAI;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// <see cref="GatewayWebAppFactory"/> variant used by the per-tool live e2e
/// tests when <c>LIVE_TEST_PREFER_AOAI=1</c>. Swaps the in-memory
/// <c>FakeModelClient</c> for a real <see cref="AzureOpenAIModelClient"/>
/// pointed at Azure OpenAI using the credentials from user-secrets or
/// environment variables.
///
/// All other test infrastructure from <see cref="GatewayWebAppFactory"/>
/// (in-memory db, JobExecutor wiring, Teams disabled) is preserved.
///
/// Configuration:
///   Reads from user-secrets (Gateway project: c15754a6-dc90-4a2a-aecb-1233d1a54fe1)
///   or environment variables:
///     Model:Endpoint        — Azure OpenAI resource URL (required)
///     Model:DeploymentName  — deployment name (default: gpt-5-mini)
///     Model:AuthMode        — "api-key" (default) or "integrated"
///     Model:ApiKey          — required when AuthMode=api-key
/// </summary>
public class LiveAoaiWebAppFactory : GatewayWebAppFactory
{
    // Gateway's UserSecretsId — set by `dotnet user-secrets init`
    private const string GatewayUserSecretsId = "c15754a6-dc90-4a2a-aecb-1233d1a54fe1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Read AOAI config from user-secrets + environment variables (user secrets
        // are already loaded by the base Gateway config; we layer in environment
        // overrides for CI/operator scenarios).
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddUserSecrets(GatewayUserSecretsId, reloadOnChange: false);
            cfg.AddEnvironmentVariables();
        });

        // Override Model:* config so any code path that constructs a
        // RuntimeModelClient (vs reading IModelClient directly) also points at
        // the live AOAI instance. This is symmetric to LiveOllamaWebAppFactory.
        builder.ConfigureAppConfiguration((context, cfg) =>
        {
            var tempConfig = cfg.Build();
            var endpoint = tempConfig["Model:Endpoint"];
            var deployment = tempConfig["Model:DeploymentName"] ?? "gpt-5-mini";
            var authMode = tempConfig["Model:AuthMode"] ?? "api-key";

            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // NOTE: Original PR #77 used "AzureOpenAI" (PascalCase). A follow-up tried
                // changing to "azure-openai" to match AzureOpenAIAgentProvider.ProviderName
                // for IAgentProvider lookups, but that REGRESSED Web/HtmlQuery/MarkItDown
                // (they pass with "AzureOpenAI" because the JobExecutor falls through to the
                // IModelClient path that we swapped via AddAzureOpenAI() below). Until the
                // IAgentProvider path on AOAI is debugged separately, keep the original name.
                ["Model:Provider"] = "AzureOpenAI",
                ["Model:Endpoint"] = endpoint,
                ["Model:DeploymentName"] = deployment,
                ["Model:AuthMode"] = authMode,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop the FakeModelClient that the base factory installed and
            // wire in a real AzureOpenAIModelClient. AddAzureOpenAI re-registers
            // IModelClient via the AzureOpenAIModelClient singleton.
            services.RemoveAll<IModelClient>();
            services.AddAzureOpenAI();
        });
    }
}
