using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Tools.FileSystem;

namespace OpenClawNet.UnitTests.Tools;

/// <summary>
/// Unit tests for FileSystemTool covering the new FindProjects action,
/// absolute-path resolution, and blocked-path enforcement.
/// </summary>
public sealed class FileSystemToolTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ocn-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private FileSystemTool CreateTool(string workspacePath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:WorkspacePath"] = workspacePath
            })
            .Build();

        return new FileSystemTool(NullLogger<FileSystemTool>.Instance, config);
    }

    private static async Task<string> ExecuteActionAsync(
        FileSystemTool tool, string action, string path, string? content = null)
    {
        var args = content is null
            ? $"{{\"action\":\"{action}\",\"path\":\"{path.Replace("\\", "\\\\")}\" }}"
            : $"{{\"action\":\"{action}\",\"path\":\"{path.Replace("\\", "\\\\")}\",\"content\":\"{content}\"}}";

        var input = new OpenClawNet.Tools.Abstractions.ToolInput
        {
            ToolName = "file_system",
            RawArguments = args
        };
        var result = await tool.ExecuteAsync(input);
        return result.Success ? result.Output : result.Error ?? "unknown error";
    }

    // ── FindProjects ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FindProjects_ReturnsProjectFiles_WhenCsprojExists()
    {
        // Arrange — create a fake solution structure
        var srcDir = Path.Combine(_tempDir, "src", "MyApp");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "MyApp.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "MyApp.sln"), "Microsoft Visual Studio Solution File");

        var tool = CreateTool(_tempDir);

        // Act
        var output = await ExecuteActionAsync(tool, "find_projects", ".");

        // Assert
        output.Should().Contain("MyApp.csproj");
        output.Should().Contain("MyApp.sln");
    }

    [Fact]
    public async Task FindProjects_ExcludesBinAndObjDirectories()
    {
        // Arrange — place a .csproj inside bin/ (should be ignored)
        var binDir = Path.Combine(_tempDir, "bin", "Debug");
        Directory.CreateDirectory(binDir);
        await File.WriteAllTextAsync(Path.Combine(binDir, "Hidden.csproj"), "<Project />");

        // Place one real project
        var realDir = Path.Combine(_tempDir, "src", "Real");
        Directory.CreateDirectory(realDir);
        await File.WriteAllTextAsync(Path.Combine(realDir, "Real.csproj"), "<Project />");

        var tool = CreateTool(_tempDir);

        var output = await ExecuteActionAsync(tool, "find_projects", ".");

        output.Should().Contain("Real.csproj");
        output.Should().NotContain("Hidden.csproj");
    }

    [Fact]
    public async Task FindProjects_ReturnsFailure_WhenDirectoryNotFound()
    {
        var tool = CreateTool(_tempDir);
        var missingPath = Path.Combine(_tempDir, "does-not-exist");

        var input = new OpenClawNet.Tools.Abstractions.ToolInput
        {
            ToolName = "file_system",
            RawArguments = $"{{\"action\":\"find_projects\",\"path\":\"{missingPath.Replace("\\", "\\\\")}\" }}"
        };
        var result = await tool.ExecuteAsync(input);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    // ── Absolute path resolution ─────────────────────────────────────────────

    [Fact]
    public async Task List_WithAbsolutePath_ListsDirectory()
    {
        // Arrange — workspace at a different root, but we provide an absolute path
        var outsideDir = Path.Combine(Path.GetTempPath(), $"ocn-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        await File.WriteAllTextAsync(Path.Combine(outsideDir, "readme.txt"), "hello");

        var tool = CreateTool(_tempDir); // workspace is _tempDir, NOT outsideDir

        try
        {
            var output = await ExecuteActionAsync(tool, "list", outsideDir);
            output.Should().Contain("AbsolutePathOutsideScope");
        }
        finally
        {
            Directory.Delete(outsideDir, recursive: true);
        }
    }

    [Fact]
    public async Task Read_WithAbsolutePath_ReadsFile()
    {
        // Write a file in _tempDir and read it with an absolute path
        var file = Path.Combine(_tempDir, "hello.txt");
        await File.WriteAllTextAsync(file, "absolute content");

        var tool = CreateTool(Path.GetTempPath()); // workspace root is parent dir

        var output = await ExecuteActionAsync(tool, "read", file);

        output.Should().Be("absolute content");
    }

    // ── Blocked paths ────────────────────────────────────────────────────────

    [Fact]
    public async Task Read_BlockedPath_ReturnsFailure()
    {
        var blockedFile = Path.Combine(_tempDir, ".env");
        await File.WriteAllTextAsync(blockedFile, "SECRET=abc");

        var tool = CreateTool(_tempDir);

        var input = new OpenClawNet.Tools.Abstractions.ToolInput
        {
            ToolName = "file_system",
            RawArguments = "{\"action\":\"read\",\"path\":\".env\"}"
        };
        var result = await tool.ExecuteAsync(input);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("blocked", Exactly.Once());
    }

    [Fact]
    public async Task Read_BlockedPath_AlsoBlocksAbsoluteVariants()
    {
        var blockedAbsolute = Path.Combine(_tempDir, ".env");
        await File.WriteAllTextAsync(blockedAbsolute, "SECRET=abc");

        var tool = CreateTool(_tempDir);

        var input = new OpenClawNet.Tools.Abstractions.ToolInput
        {
            ToolName = "file_system",
            RawArguments = $"{{\"action\":\"read\",\"path\":\"{blockedAbsolute.Replace("\\", "\\\\")}\"}}"
        };
        var result = await tool.ExecuteAsync(input);

        result.Success.Should().BeFalse();
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_RelativePath_ListsWorkspaceDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "foo.cs"), "// code");
        var tool = CreateTool(_tempDir);

        var output = await ExecuteActionAsync(tool, "list", ".");

        output.Should().Contain("foo.cs");
    }

    // ── Write & Read roundtrip ───────────────────────────────────────────────

    [Fact]
    public async Task Write_ThenRead_RoundTrips()
    {
        var tool = CreateTool(_tempDir);
        var filePath = Path.Combine(_tempDir, "roundtrip.txt");

        var args = $"{{\"action\":\"write\",\"path\":\"{filePath.Replace("\\", "\\\\")}\",\"content\":\"hello world\"}}";
        var writeResult = await tool.ExecuteAsync(new OpenClawNet.Tools.Abstractions.ToolInput
        {
            ToolName = "file_system",
            RawArguments = args
        });
        writeResult.Success.Should().BeTrue();

        var readResult = await ExecuteActionAsync(tool, "read", filePath);
        readResult.Should().Be("hello world");
    }
}
