using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawNet.Gateway.Configuration;
using OpenClawNet.Gateway.Services;
using System.Runtime.InteropServices;

namespace OpenClawNet.UnitTests.Services;

public class StorageDirectoryProviderTests : IDisposable
{
    private readonly List<string> _createdDirectories = new();

    public void Dispose()
    {
        // Cleanup created test directories
        foreach (var dir in _createdDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void GetStorageDirectory_UsesPlatformDefault_WhenConfigIsNull()
    {
        // Arrange
        var options = Options.Create(new OpenClawNetOptions { StorageDir = null });
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);

        // Act
        var result = provider.GetStorageDirectory("test-agent");
        _createdDirectories.Add(result);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("test-agent", result);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Contains("OpenClawNet", result);
            var expectedBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OpenClawNet");
            Assert.StartsWith(expectedBase, result);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir))
                Assert.Contains(".openclawnet", result);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Contains("Library", result);
            Assert.Contains("OpenClawNet", result);
        }

        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void GetStorageDirectory_UsesConfigValue_WhenProvided()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), $"openclawnet-test-{Guid.NewGuid():N}");
        var options = Options.Create(new OpenClawNetOptions { StorageDir = tempBase });
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);

        // Act
        var result = provider.GetStorageDirectory("config-agent");
        _createdDirectories.Add(tempBase);

        // Assert
        Assert.StartsWith(tempBase, result);
        Assert.EndsWith("config-agent", result);
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void GetStorageDirectory_UsesEnvironmentVariable_WhenSet()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), $"openclawnet-env-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("OPENCLAW_STORAGE_DIR", tempBase);
        
        try
        {
            var options = Options.Create(new OpenClawNetOptions { StorageDir = "should-be-ignored" });
            var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
            var provider = new StorageDirectoryProvider(options, logger);

            // Act
            var result = provider.GetStorageDirectory("env-agent");
            _createdDirectories.Add(tempBase);

            // Assert
            Assert.StartsWith(tempBase, result);
            Assert.EndsWith("env-agent", result);
            Assert.True(Directory.Exists(result));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCLAW_STORAGE_DIR", null);
        }
    }

    [Fact]
    public void GetStorageDirectory_CreatesNestedDirectories_WhenMissing()
    {
        // Arrange
        var tempBase = Path.Combine(
            Path.GetTempPath(), 
            $"openclawnet-nested-{Guid.NewGuid():N}",
            "level1",
            "level2");
        var options = Options.Create(new OpenClawNetOptions { StorageDir = tempBase });
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);

        // Act
        var result = provider.GetStorageDirectory("nested-agent");
        _createdDirectories.Add(Path.Combine(Path.GetTempPath(), result.Split(Path.DirectorySeparatorChar)[0]));

        // Assert
        Assert.True(Directory.Exists(result));
        Assert.Contains("nested-agent", result);
    }

    [Fact]
    public void GetStorageDirectory_ThrowsArgumentNullException_WhenAgentNameIsNull()
    {
        // Arrange
        var options = Options.Create(new OpenClawNetOptions());
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => provider.GetStorageDirectory(null!));
    }

    [Fact]
    public void GetStorageDirectory_ThrowsArgumentNullException_WhenAgentNameIsEmpty()
    {
        // Arrange
        var options = Options.Create(new OpenClawNetOptions());
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => provider.GetStorageDirectory(string.Empty));
    }

    [Fact]
    public void GetStorageDirectory_IsConcurrentSafe_MultipleAgentsSimultaneously()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), $"openclawnet-concurrent-{Guid.NewGuid():N}");
        var options = Options.Create(new OpenClawNetOptions { StorageDir = tempBase });
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);
        var agentNames = Enumerable.Range(1, 20).Select(i => $"agent-{i}").ToList();

        // Act - Concurrent access from multiple threads
        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(agentNames, agentName =>
        {
            var path = provider.GetStorageDirectory(agentName);
            results.Add(path);
        });

        _createdDirectories.Add(tempBase);

        // Assert - All directories created successfully
        Assert.Equal(agentNames.Count, results.Count);
        foreach (var result in results)
        {
            Assert.True(Directory.Exists(result));
        }

        // All agent names represented
        var createdAgents = results.Select(r => Path.GetFileName(r)).ToHashSet();
        Assert.Equal(agentNames.Count, createdAgents.Count);
    }

    [Fact]
    public void GetStorageDirectory_ReturnsSamePath_ForSameAgent()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), $"openclawnet-idempotent-{Guid.NewGuid():N}");
        var options = Options.Create(new OpenClawNetOptions { StorageDir = tempBase });
        var logger = Mock.Of<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, logger);

        // Act
        var result1 = provider.GetStorageDirectory("same-agent");
        var result2 = provider.GetStorageDirectory("same-agent");
        _createdDirectories.Add(tempBase);

        // Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void GetStorageDirectory_FallsBackToTemp_OnUnauthorizedAccess()
    {
        // Arrange - Use a path that requires admin permissions on Windows
        string restrictedPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            restrictedPath = @"C:\Windows\System32\OpenClawNet"; // Requires admin
        }
        else
        {
            restrictedPath = "/root/openclawnet"; // Requires root on Linux/macOS
        }
        
        var options = Options.Create(new OpenClawNetOptions { StorageDir = restrictedPath });
        var mockLogger = new Mock<ILogger<StorageDirectoryProvider>>();
        var provider = new StorageDirectoryProvider(options, mockLogger.Object);

        // Act
        var result = provider.GetStorageDirectory("fallback-agent");
        _createdDirectories.Add(result);

        // Assert - Should fallback to temp directory
        Assert.Contains(Path.GetTempPath(), result);
        Assert.Contains("fallback-agent", result);
        Assert.True(Directory.Exists(result));
        
        // Verify warning was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Permission denied") || v.ToString()!.Contains("fallback")),
                It.IsAny<UnauthorizedAccessException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "should log warning about permission denied and fallback");
    }

    [Fact]
    public void Validate_ThrowsArgumentException_ForInvalidPathCharacters()
    {
        // Arrange - use null character which is universally invalid
        var options = new OpenClawNetOptions
        {
            StorageDir = "invalid\0path"
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("invalid path characters", ex.Message);
    }

    [Fact]
    public void Validate_DoesNotThrow_ForValidPath()
    {
        // Arrange
        var options = new OpenClawNetOptions
        {
            StorageDir = Path.Combine(Path.GetTempPath(), "valid-path")
        };

        // Act & Assert (should not throw)
        options.Validate();
    }

    [Fact]
    public void Validate_DoesNotThrow_ForNullStorageDir()
    {
        // Arrange
        var options = new OpenClawNetOptions
        {
            StorageDir = null
        };

        // Act & Assert (should not throw)
        options.Validate();
    }

    [Fact]
    public void StorageRetentionOptions_HasDefaultValues()
    {
        // Arrange & Act
        var options = new StorageRetentionOptions();

        // Assert
        Assert.False(options.Enabled);
        Assert.Equal(30, options.MaxAgeInDays);
    }
}
