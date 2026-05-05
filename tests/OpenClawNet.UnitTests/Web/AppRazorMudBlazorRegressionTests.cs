using FluentAssertions;
using Xunit;

namespace OpenClawNet.UnitTests.Web;

/// <summary>
/// Regression test: ensures both Channels and Web App.razor files contain
/// the MudBlazor CSS and JS bundle references. Previously, Channels App.razor
/// was missing these references, causing MudBlazor components to fail to render.
/// </summary>
public class AppRazorMudBlazorRegressionTests
{
    private const string MudBlazorCss = "_content/MudBlazor/MudBlazor.min.css";
    private const string MudBlazorJs = "_content/MudBlazor/MudBlazor.min.js";

    [Trait("Category", "Unit")]
    [Fact]
    public void WebAppRazor_ContainsMudBlazorBundles()
    {
        var appRazorPath = GetRepositoryPath("src", "OpenClawNet.Web", "Components", "App.razor");
        File.Exists(appRazorPath).Should().BeTrue($"App.razor should exist at {appRazorPath}");

        var content = File.ReadAllText(appRazorPath);

        content.Should().Contain(MudBlazorCss, "Web App.razor must reference MudBlazor CSS bundle");
        content.Should().Contain(MudBlazorJs, "Web App.razor must reference MudBlazor JS bundle");
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ChannelsAppRazor_ContainsMudBlazorBundles()
    {
        var appRazorPath = GetRepositoryPath("src", "OpenClawNet.Channels", "Components", "App.razor");
        File.Exists(appRazorPath).Should().BeTrue($"App.razor should exist at {appRazorPath}");

        var content = File.ReadAllText(appRazorPath);

        content.Should().Contain(MudBlazorCss, "Channels App.razor must reference MudBlazor CSS bundle");
        content.Should().Contain(MudBlazorJs, "Channels App.razor must reference MudBlazor JS bundle");
    }

    /// <summary>
    /// Walks up from the test assembly location to find the repository root
    /// (directory containing OpenClawNet.slnx), then builds the full path.
    /// </summary>
    private static string GetRepositoryPath(params string[] segments)
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir.FullName, "OpenClawNet.slnx")))
            {
                return Path.Combine(new[] { currentDir.FullName }.Concat(segments).ToArray());
            }
            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (OpenClawNet.slnx not found)");
    }
}
