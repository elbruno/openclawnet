using GitHub.Copilot;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Models.GitHubCopilot;

/// <summary>
/// <see cref="IAgentProvider"/> backed by the GitHub Copilot SDK.
/// Manages a shared <see cref="CopilotClient"/> (lazy-started on first use) and
/// returns <see cref="CopilotChatClient"/> instances from <see cref="CreateChatClient"/>.
/// </summary>
public sealed class GitHubCopilotAgentProvider : IAgentProvider, IAsyncDisposable
{
    private readonly IOptions<GitHubCopilotOptions> _options;
    private readonly ILogger<GitHubCopilotAgentProvider> _logger;

    private CopilotClient? _client;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _disposed;
    private string? _overrideToken;

    public GitHubCopilotAgentProvider(
        IOptions<GitHubCopilotOptions> options,
        ILogger<GitHubCopilotAgentProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => "github-copilot";

    public IChatClient CreateChatClient(AgentProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Use token from profile (ModelProviderDefinition.ApiKey) if available
        if (!string.IsNullOrEmpty(profile.ApiKey))
        {
            if (_overrideToken != profile.ApiKey)
            {
                _overrideToken = profile.ApiKey;
                _client = null; // Force client recreation with new token
            }
        }

        var model = _options.Value.Model;
        var innerClient = new CopilotChatClient(this, model, profile.Instructions);
        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(sourceName: "OpenClawNet.GitHubCopilot")
            .Build();
    }

    /// <summary>
    /// Returns the shared <see cref="CopilotClient"/>, starting it on first call.
    /// Thread-safe via double-check with <see cref="SemaphoreSlim"/>.
    /// </summary>
    internal async Task<CopilotClient> GetClientAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client is not null)
            return _client;

        await _startLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is not null)
                return _client;

            var opts = _options.Value;
            var token = _overrideToken ?? opts.GitHubToken;
            var clientOptions = BuildClientOptions(opts, _logger, token);

            _logger.LogInformation("Starting Copilot SDK client (model={Model})", opts.Model);

            var client = new CopilotClient(clientOptions);
            await client.StartAsync().ConfigureAwait(false);

            _client = client;
            return _client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Copilot SDK client");
            throw;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // If a token is explicitly configured or overridden from profile, consider the provider available.
        if (!string.IsNullOrEmpty(_overrideToken))
            return true;
        if (!string.IsNullOrEmpty(_options.Value.GitHubToken))
            return true;

        // Check common environment variables used by the SDK.
        var envTokens = new[] { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" };
        if (envTokens.Any(v => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))))
            return true;

        // Fall back to checking `gh auth status` for a logged-in user.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("gh", "auth status")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GitHub CLI not available — Copilot provider unavailable");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_client is not null)
        {
            try
            {
                await _client.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping Copilot SDK client");
            }
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        _startLock.Dispose();
    }

    internal static CopilotClientOptions BuildClientOptions(
        GitHubCopilotOptions options,
        ILogger logger,
        string? token)
    {
        var clientOptions = new CopilotClientOptions
        {
            Logger = logger,
        };

        if (!string.IsNullOrEmpty(token))
            clientOptions.GitHubToken = token;

        if (!string.IsNullOrEmpty(options.CliPath))
            clientOptions.Connection = RuntimeConnection.ForStdio(path: options.CliPath);

        return clientOptions;
    }
}
