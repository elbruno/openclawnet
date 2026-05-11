using Microsoft.Extensions.Options;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Storage;

public sealed class EnvironmentSecretsStoreTests
{
    [Fact]
    public async Task GetAsync_PrefersEnvVar_OverDockerSecretFile()
    {
        var name = $"EnvSecret_{Guid.NewGuid():N}";
        var envKey = $"{EnvironmentSecretsStoreOptions.DefaultPrefix}{NormalizeEnvKey(name)}";
        var tempDir = Path.Combine(Environment.CurrentDirectory, "TestResults", $"envstore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, NormalizeFileName(name));

        try
        {
            Environment.SetEnvironmentVariable(envKey, "env-value");
            await File.WriteAllTextAsync(filePath, "file-value");
            var store = CreateStore(tempDir);

            var value = await store.GetAsync(name);

            Assert.Equal("env-value", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetAsync_UsesDockerSecretFile_WhenEnvVarMissing()
    {
        var name = $"FileSecret_{Guid.NewGuid():N}";
        var tempDir = Path.Combine(Environment.CurrentDirectory, "TestResults", $"envstore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, NormalizeFileName(name));

        try
        {
            await File.WriteAllTextAsync(filePath, "file-value");
            var store = CreateStore(tempDir);

            var value = await store.GetAsync(name);

            Assert.Equal("file-value", value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_OnlyIncludesPrefixedEnvVars()
    {
        var goodName = $"Good_{Guid.NewGuid():N}";
        var goodKey = $"{EnvironmentSecretsStoreOptions.DefaultPrefix}{NormalizeEnvKey(goodName)}";
        var badKey = $"OTHER_{Guid.NewGuid():N}";

        try
        {
            Environment.SetEnvironmentVariable(goodKey, "good");
            Environment.SetEnvironmentVariable(badKey, "bad");
            var store = CreateStore(Path.Combine(Environment.CurrentDirectory, "TestResults", $"envstore-{Guid.NewGuid():N}"));

            var list = await store.ListAsync();

            Assert.Contains(list, item => item.Name == NormalizeEnvKey(goodName));
            Assert.DoesNotContain(list, item => item.Name == NormalizeEnvKey(badKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(goodKey, null);
            Environment.SetEnvironmentVariable(badKey, null);
        }
    }

    [Fact]
    public async Task ListAsync_DedupesEnvVarAndFileNames()
    {
        var name = $"Dup_{Guid.NewGuid():N}";
        var envKey = $"{EnvironmentSecretsStoreOptions.DefaultPrefix}{NormalizeEnvKey(name)}";
        var tempDir = Path.Combine(Environment.CurrentDirectory, "TestResults", $"envstore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, NormalizeFileName(name));

        try
        {
            Environment.SetEnvironmentVariable(envKey, "env");
            await File.WriteAllTextAsync(filePath, "file");
            var store = CreateStore(tempDir);

            var list = await store.ListAsync();

            Assert.Single(list, item => item.Name == NormalizeEnvKey(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteOperations_ThrowNotSupported()
    {
        var store = CreateStore(Path.Combine(Environment.CurrentDirectory, "TestResults", $"envstore-{Guid.NewGuid():N}"));

        await Assert.ThrowsAsync<NotSupportedException>(() => store.SetAsync("Name", "value"));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.DeleteAsync("Name"));
    }

    private static EnvironmentSecretsStore CreateStore(string dockerSecretsPath)
    {
        var options = Options.Create(new EnvironmentSecretsStoreOptions
        {
            DockerSecretsPath = dockerSecretsPath
        });
        return new EnvironmentSecretsStore(options);
    }

    private static string NormalizeEnvKey(string name)
    {
        return string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_'));
    }

    private static string NormalizeFileName(string name)
    {
        return string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));
    }
}
