using System.Diagnostics;
using System.Text;

namespace OpenClawNet.DeploymentTests.Fixtures;

/// <summary>
/// Fixture that manages docker-compose lifecycle for deployment tests.
/// Handles setup (docker-compose up), teardown (docker-compose down),
/// and provides utilities for log capture and container introspection.
/// </summary>
public sealed class DockerComposeFixture : IAsyncLifetime
{
    private readonly string _composePath;
    private readonly Dictionary<string, string> _environmentVariables;
    private readonly List<string> _capturedLogs = [];
    private bool _isRunning;
    private bool _dockerAvailable;

    public DockerComposeFixture(string composePath = ".", Dictionary<string, string>? environmentVariables = null)
    {
        _composePath = Path.GetFullPath(composePath);
        _environmentVariables = environmentVariables ?? new();
        _isRunning = false;
        _dockerAvailable = false;
    }

    /// <summary>Gets the captured logs from the last docker-compose logs call.</summary>
    public IReadOnlyList<string> CapturedLogs => _capturedLogs.AsReadOnly();

    /// <summary>Gets whether Docker and docker-compose are available on this system.</summary>
    public bool IsDockerAvailable => _dockerAvailable;

    /// <summary>Gets whether services are currently running.</summary>
    public bool IsRunning => _isRunning;

    public async Task InitializeAsync()
    {
        // Verify Docker is available
        _dockerAvailable = await CheckDockerAvailabilityAsync();
        
        if (_dockerAvailable)
        {
            // Start docker-compose services
            await RunDockerComposeAsync("up", "-d");
            _isRunning = true;
            
            // Capture initial logs
            await CaptureLogsAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_isRunning && _dockerAvailable)
        {
            try
            {
                // Stop and remove containers
                await RunDockerComposeAsync("down");
                _isRunning = false;
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't fail the test
                _capturedLogs.Add($"[ERROR] Failed to run docker-compose down: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Runs a docker-compose command and returns the output.
    /// Throws InvalidOperationException if docker-compose is not available.
    /// </summary>
    public async Task<string> RunDockerComposeAsync(params string[] args)
    {
        if (!_dockerAvailable)
        {
            throw new InvalidOperationException("Docker is not available");
        }

        var allArgs = string.Join(" ", args.Select(arg => $"\"{arg}\""));
        var processInfo = new ProcessStartInfo
        {
            FileName = "docker-compose",
            Arguments = allArgs,
            WorkingDirectory = _composePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Add environment variables
        foreach (var (key, value) in _environmentVariables)
        {
            processInfo.EnvironmentVariables[key] = value;
        }

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start docker-compose process");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker-compose {args[0]} failed with exit code {process.ExitCode}:\n{stderr}");
        }

        return stdout;
    }

    /// <summary>
    /// Captures logs from all running containers using docker-compose logs.
    /// Appends to the internal log buffer.
    /// </summary>
    public async Task CaptureLogsAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            var logs = await RunDockerComposeAsync("logs");
            _capturedLogs.Clear();
            _capturedLogs.AddRange(logs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex)
        {
            _capturedLogs.Add($"[ERROR] Failed to capture logs: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if there are any ERROR-level log lines in the captured logs.
    /// Returns the error lines found, or an empty list if none.
    /// </summary>
    public IEnumerable<string> GetErrorLogs()
    {
        return _capturedLogs.Where(line => line.Contains("ERROR", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that a specific service/container name is running.
    /// </summary>
    public async Task<bool> IsContainerRunningAsync(string containerName)
    {
        if (!_dockerAvailable)
        {
            return false;
        }

        try
        {
            var output = await RunCommandAsync("docker", "ps", "--format", "{{.Names}}");
            return output.Contains(containerName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the environment variables set on a running container.
    /// </summary>
    public async Task<Dictionary<string, string>> GetContainerEnvAsync(string containerName)
    {
        var result = new Dictionary<string, string>();

        try
        {
            var output = await RunCommandAsync("docker", "inspect", "-f", "{{json .Config.Env}}", containerName);
            var lines = System.Text.Json.JsonDocument.Parse(output);
            foreach (var element in lines.RootElement.EnumerateArray())
            {
                var envVar = element.GetString();
                if (!string.IsNullOrEmpty(envVar))
                {
                    var parts = envVar.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        result[parts[0]] = parts[1];
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _capturedLogs.Add($"[ERROR] Failed to get container env: {ex.Message}");
        }

        return result;
    }

    private async Task<bool> CheckDockerAvailabilityAsync()
    {
        try
        {
            await RunCommandAsync("docker", "info");
            await RunCommandAsync("docker-compose", "--version");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> RunCommandAsync(params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = args[0],
            Arguments = string.Join(" ", args.Skip(1).Select(arg => $"\"{arg}\"")),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start {args[0]} process");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{args[0]} exited with code {process.ExitCode}");
        }

        return stdout;
    }
}
