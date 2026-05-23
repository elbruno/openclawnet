using System.Collections;
using System.Text;
using Microsoft.Extensions.Options;

namespace OpenClawNet.Storage;

public sealed class EnvironmentSecretsStoreOptions
{
    public const string SectionName = "Vault:Environment";
    public const string DefaultPrefix = "OPENCLAWNET_SECRET_";
    public const string DefaultDockerSecretsPath = "/run/secrets";

    public string Prefix { get; set; } = DefaultPrefix;
    public string DockerSecretsPath { get; set; } = DefaultDockerSecretsPath;
}

/// <summary>Read-only secrets store backed by env vars + Docker secrets files.</summary>
public sealed class EnvironmentSecretsStore : ISecretsStore
{
    private readonly EnvironmentSecretsStoreOptions _options;

    public EnvironmentSecretsStore(IOptions<EnvironmentSecretsStoreOptions> options)
    {
        _options = options?.Value ?? new EnvironmentSecretsStoreOptions();
        if (string.IsNullOrWhiteSpace(_options.Prefix))
            _options.Prefix = EnvironmentSecretsStoreOptions.DefaultPrefix;
        if (string.IsNullOrWhiteSpace(_options.DockerSecretsPath))
            _options.DockerSecretsPath = EnvironmentSecretsStoreOptions.DefaultDockerSecretsPath;
    }

    public Task SetAsync(string name, string value, string? description = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Environment secrets store is read-only.");

    public Task<bool> DeleteAsync(string name, CancellationToken ct = default) =>
        throw new NotSupportedException("Environment secrets store is read-only.");

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var envKey = $"{_options.Prefix}{NormalizeEnvKey(name)}";
        var envValue = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(envValue))
            return envValue;

        var fileName = NormalizeFileName(name);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var filePath = Path.Combine(_options.DockerSecretsPath, fileName);
        if (!File.Exists(filePath))
            return null;

        var value = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return value.TrimEnd('\r', '\n');
    }

    public Task<IReadOnlyList<SecretSummary>> ListAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, SecretSummary>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key)
                continue;

            if (!key.StartsWith(_options.Prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = key[_options.Prefix.Length..];
            if (string.IsNullOrWhiteSpace(suffix))
                continue;

            var name = NormalizeEnvKey(suffix);
            if (results.ContainsKey(name))
                continue;

            results[name] = new SecretSummary(name, "Environment variable", DateTime.UtcNow);
        }

        if (Directory.Exists(_options.DockerSecretsPath))
        {
            foreach (var file in Directory.EnumerateFiles(_options.DockerSecretsPath))
            {
                var fileName = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                var name = NormalizeEnvKey(fileName);
                if (results.ContainsKey(name))
                    continue;

                var updatedAt = File.GetLastWriteTimeUtc(file);
                results[name] = new SecretSummary(name, "Docker secret file", updatedAt);
            }
        }

        var ordered = results.Values
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<SecretSummary>>(ordered);
    }

    private static string NormalizeEnvKey(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
            else
                builder.Append('_');
        }
        return builder.ToString();
    }

    private static string NormalizeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else
                builder.Append('-');
        }
        return builder.ToString();
    }
}
