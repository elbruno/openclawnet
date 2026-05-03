using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Storage;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.ImageEdit;
using OpenClawNet.UnitTests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OpenClawNet.UnitTests.Tools;

public class ImageEditToolTests : IDisposable
{
    private readonly PerTestTempDirectory _temp = new("openclaw-imgedit-tests");
    private string _tempRoot => _temp.Path;
    private readonly StorageOptions _storage;

    public ImageEditToolTests()
    {
        _storage = new StorageOptions
        {
            RootPath = _tempRoot,
            BinaryFolderName = "binary",
            ModelsFolderName = "models"
        };
    }

    public void Dispose() => _temp.Dispose();

    private string CreateRedPng(int width = 40, int height = 30)
    {
        var path = Path.Combine(_tempRoot, "input.png");
        using var img = new Image<Rgba32>(width, height, new Rgba32(255, 0, 0, 255));
        using var fs = File.Create(path);
        img.Save(fs, new PngEncoder());
        return path;
    }

    private static ToolInput Args(string json) => new() { ToolName = "image_edit", RawArguments = json };

    [Fact]
    public async Task Resize_Produces_Smaller_Png()
    {
        var input = CreateRedPng();
        var tool = new ImageEditTool(_storage, NullLogger<ImageEditTool>.Instance);
        var result = await tool.ExecuteAsync(Args($$"""{ "action": "resize", "input": "{{input.Replace("\\", "\\\\")}}", "width": 10, "height": 10 }"""));
        Assert.True(result.Success, result.Error);
        Assert.Contains("Final size: 10x10", result.Output);
    }

    [Fact]
    public async Task Crop_Without_Dimensions_Fails()
    {
        var input = CreateRedPng();
        var tool = new ImageEditTool(_storage, NullLogger<ImageEditTool>.Instance);
        var result = await tool.ExecuteAsync(Args($$"""{ "action": "crop", "input": "{{input.Replace("\\", "\\\\")}}" }"""));
        Assert.False(result.Success);
        Assert.Contains("crop", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_Input_Fails()
    {
        var tool = new ImageEditTool(_storage, NullLogger<ImageEditTool>.Instance);
        var result = await tool.ExecuteAsync(Args("""{ "action": "resize", "input": "C:\\does\\not\\exist.png", "width": 10, "height": 10 }"""));
        Assert.False(result.Success);
    }
}
