using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Models.Ollama;

namespace OpenClawNet.IntegrationTests.Jobs;

/// <summary>
/// <see cref="GatewayWebAppFactory"/> variant used by the per-tool live e2e
/// tests. Swaps the in-memory <c>FakeModelClient</c> for a real
/// <see cref="OllamaModelClient"/> pointed at a local Ollama daemon
/// (default <c>http://localhost:11434</c>) using the requested model.
///
/// All other test infrastructure from <see cref="GatewayWebAppFactory"/>
/// (in-memory db, JobExecutor wiring, Teams disabled) is preserved.
/// </summary>
public class LiveOllamaWebAppFactory : GatewayWebAppFactory
{
    private readonly string _model;
    private readonly string _endpoint;

    public LiveOllamaWebAppFactory(string model = "qwen2.5:3b", string? endpoint = null)
    {
        _model = model;
        _endpoint = endpoint ?? "http://localhost:11434";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Override Model:* config so any code path that constructs a
        // RuntimeModelClient (vs reading IModelClient directly) also points at
        // the live Ollama instance with the right model name. Without this,
        // RuntimeModelSettings keeps a stale model name and Ollama returns 404.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Model:Provider"] = "ollama",
                ["Model:Model"] = _model,
                ["Model:Endpoint"] = _endpoint,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Drop the FakeModelClient that the base factory installed and
            // wire in a real OllamaModelClient. AddOllama re-registers
            // IModelClient via the OllamaModelClient typed HttpClient.
            services.RemoveAll<IModelClient>();
            services.AddOllama(opts =>
            {
                opts.Model = _model;
                opts.Endpoint = _endpoint;
            });
        });
    }
}
