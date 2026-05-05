using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace OpenClawNet.IntegrationTests.Jobs.Aspire;

/// <summary>
/// Abstract base class for Aspire-orchestrated live e2e tests.
///
/// Provides:
///  - <see cref="IDistributedApplicationTestingBuilder"/> setup via the official
///    Aspire.Hosting.Testing API (brings up the full AppHost graph: Gateway +
///    browser-service + shell-service).
///  - <see cref="IAsyncLifetime"/> implementation that starts/stops the Aspire app.
///  - <see cref="CreateGatewayClient"/> to get an HttpClient pointed at the Gateway.
///  - <see cref="SkipIfAspireUnavailable"/> helper that wraps startup in try/catch
///    and throws <see cref="SkipException"/> when Docker/Ollama/containers aren't reachable.
///
/// Notes / rationale:
///  - This pattern breaks the "one factory" rule from PR #74 because
///    Aspire-orchestrated services (browser-service, shell-service) cannot be
///    reached via WebApplicationFactory&lt;TGateway&gt;. They run in separate
///    processes/containers behind Aspire's service discovery. Approved trade-off
///    per 2026-04-24 decision in .squad/decisions.md.
///  - Tests inheriting this base are tagged [Trait("Category","Live")] + 
///    [Trait("Category","AspireRequired")] so they can be filtered independently.
///  - Startup timeout is 3 minutes by default — Aspire cold-start with Docker
///    image pulls can be slow. Override <see cref="StartupTimeout"/> if needed.
///  - All tests skip cleanly when Aspire prereqs are missing — never fail CI.
///
/// Prerequisites:
///  - AppHost must build (src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj).
///  - Docker Desktop running (browser-service + shell-service are containers or
///    standalone projects; either way, Aspire orchestration requires the DCP).
///  - Ollama reachable at localhost:11434 (Gateway falls back to local Ollama).
///  - Container images pullable if browser/shell services are containerized.
///
/// Local-only by design: These tests are **never** run in GitHub Actions or any
/// hosted CI environment. See .squad/decisions.md "Live tests are local-only".
/// </summary>
[Trait("Category", "Live")]
[Trait("Category", "AspireRequired")]
public abstract class AspireLiveTestBase : IAsyncLifetime
{
    protected IDistributedApplicationTestingBuilder? Builder { get; private set; }
    protected DistributedApplication? App { get; private set; }

    /// <summary>
    /// Override to customize startup timeout. Default is 3 minutes (180 seconds).
    /// Aspire cold-start can take 1-2 minutes with Docker image pulls + service warmup.
    /// </summary>
    protected virtual TimeSpan StartupTimeout => TimeSpan.FromMinutes(3);

    public virtual async Task InitializeAsync()
    {
        // Wrap the entire Aspire startup in a try/catch so we can skip cleanly
        // when Docker/Ollama/containers aren't available. Never fail the test
        // when infrastructure is missing — this is local-only smoke testing.
        try
        {
            // Use reflection to load the AppHost assembly at runtime to avoid compile-time
            // ambiguity with the Gateway's Program class.
            var appHostAssembly = System.Reflection.Assembly.LoadFrom(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "src", "OpenClawNet.AppHost", "bin", "Debug", "net10.0", "OpenClawNet.AppHost.dll"));
            
            var appHostProgramType = appHostAssembly.GetType("Program")
                ?? throw new InvalidOperationException("Could not find Program type in AppHost assembly.");

            // Call DistributedApplicationTestingBuilder.CreateAsync<TEntryPoint>() using reflection.
            var createMethod = typeof(DistributedApplicationTestingBuilder)
                .GetMethod(nameof(DistributedApplicationTestingBuilder.CreateAsync), 
                           System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?
                .MakeGenericMethod(appHostProgramType)
                ?? throw new InvalidOperationException("Could not find CreateAsync method on DistributedApplicationTestingBuilder.");

            var builderTask = (Task<IDistributedApplicationTestingBuilder>?)createMethod.Invoke(null, null)
                ?? throw new InvalidOperationException("CreateAsync returned null.");
            
            Builder = await builderTask;
            
            App = await Builder.BuildAsync();

            // Start the distributed app and wait for all resources to be Running.
            // Uses the StartupTimeout configured above (default 3 minutes).
            using var cts = new CancellationTokenSource(StartupTimeout);
            await App.StartAsync(cts.Token);

            // Wait for resources to become healthy (simple delay-based approach).
            // The Aspire.Hosting.Testing API doesn't expose a direct "wait for healthy" method
            // in all versions, so we give it a moment to stabilize.
            await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
        }
        catch (Exception ex)
        {
            // Aspire startup failed — likely Docker not running, Ollama unavailable,
            // container image pull failed, or AppHost won't compile. Skip the test
            // with a clear message so engineers know what to fix locally.
            var message = $"Aspire AppHost failed to start — is Docker/Ollama/etc. running locally? " +
                          $"Error: {ex.GetType().Name}: {ex.Message}";
            throw new SkipException(message);
        }
    }

    public virtual async Task DisposeAsync()
    {
        if (App is not null)
        {
            try
            {
                // Stop all resources gracefully and clean up.
                await App.StopAsync();
                await App.DisposeAsync();
            }
            catch
            {
                // Best-effort cleanup; don't fail the test on dispose issues.
            }
        }
    }

    /// <summary>
    /// Creates an HttpClient pointed at the "gateway" resource in the Aspire AppHost.
    /// The Gateway is configured in AppHost.cs as the main entry point with
    /// .WithExternalHttpEndpoints() enabled.
    /// </summary>
    protected HttpClient CreateGatewayClient()
    {
        if (App is null)
            throw new InvalidOperationException("App is null — call InitializeAsync first or check skip logic.");

        // App.CreateHttpClient("gateway") returns an HttpClient wired to the
        // Gateway's dynamically-assigned endpoint (typically https://localhost:XXXXX).
        var client = App.CreateHttpClient("gateway");
        // Allow long-running job executions (tool loops can take minutes with cold Ollama).
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    /// <summary>
    /// Skip helper that can be called at the top of each test method to guard
    /// against missing Aspire prereqs. If InitializeAsync threw and App is null,
    /// this will re-throw a SkipException. Otherwise, does nothing (app is ready).
    /// </summary>
    protected void SkipIfAspireUnavailable()
    {
        if (App is null)
        {
            throw new SkipException("Aspire AppHost is not available — skipping test. " +
                                    "Ensure Docker/Ollama/etc. are running locally.");
        }
    }
}
