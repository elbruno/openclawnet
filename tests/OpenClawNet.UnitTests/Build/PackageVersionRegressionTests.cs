using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.UnitTests.Build;

/// <summary>
/// Regression tests for issue #202 — NU1605 package downgrade failures.
///
/// Several projects (OpenClawNet.Memory, OpenClawNet.Tools.FileSystem,
/// OpenClawNet.Tools.GitHub, OpenClawNet.Mcp.FileSystem) pinned
/// Microsoft.Extensions.* to versions below what their transitive
/// dependencies required, causing NU1605 warnings-as-errors at build time.
///
/// These tests guard against that regression by:
///   1. Confirming the loaded assemblies' file/informational versions are >= 10.0.10.
///      (AssemblyVersion is always 10.0.0.0 in .NET, so file version is used instead.)
///   2. Exercising the DI abstractions end-to-end.
/// </summary>
public sealed class PackageVersionRegressionTests
{
    // Minimum NuGet patch version required by transitive deps (issue #202).
    // 3-part version so that parsed informational strings like "10.0.10" (revision=-1)
    // compare correctly; Version(10,0,10,-1) < Version(10,0,10,0) in .NET.
    private static readonly Version MinVersion = new(10, 0, 10);

    // ── Assembly version assertions (using FileVersion, not AssemblyVersion) ──
    //
    // Microsoft.Extensions.* AssemblyVersion is pinned to 10.0.0.0 for
    // binding-redirect stability; the actual NuGet patch is in FileVersionInfo.

    [Fact(DisplayName = "Microsoft.Extensions.DependencyInjection.Abstractions >= 10.0.10 (issue #202)")]
    public void DependencyInjectionAbstractions_FileVersion_IsAtLeast_10_0_10()
    {
        AssertFileVersionAtLeast(typeof(IServiceCollection),
            "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    [Fact(DisplayName = "Microsoft.Extensions.Logging.Abstractions >= 10.0.10 (issue #202)")]
    public void LoggingAbstractions_FileVersion_IsAtLeast_10_0_10()
    {
        AssertFileVersionAtLeast(typeof(ILogger),
            "Microsoft.Extensions.Logging.Abstractions");
    }

    [Fact(DisplayName = "Microsoft.Extensions.Configuration.Abstractions >= 10.0.10 (issue #202)")]
    public void ConfigurationAbstractions_FileVersion_IsAtLeast_10_0_10()
    {
        AssertFileVersionAtLeast(typeof(IConfiguration),
            "Microsoft.Extensions.Configuration.Abstractions");
    }

    // ── DI end-to-end smoke for affected packages ─────────────────────────────

    [Fact(DisplayName = "IServiceCollection resolves ILoggerFactory via NullLoggerFactory (issue #202)")]
    public void ServiceCollection_WithNullLoggerFactory_Resolves_ILoggerFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILoggerFactory>().Should().NotBeNull();
        provider.GetRequiredService<ILogger<PackageVersionRegressionTests>>().Should().NotBeNull();
    }

    [Fact(DisplayName = "IConfiguration.GetSection round-trips in-memory values (issue #202)")]
    public void ConfigurationBuilder_GetSection_ReturnsExpectedValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Foo:Bar"] = "baz" })
            .Build();

        config["Foo:Bar"].Should().Be("baz");
        config.GetSection("Foo")["Bar"].Should().Be("baz");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static void AssertFileVersionAtLeast(Type representativeType, string assemblyName)
    {
        var asm = representativeType.Assembly;
        asm.GetName().Name.Should().Be(assemblyName,
            because: "the representative type must come from the expected assembly");

        // Prefer the informational version (e.g. "10.0.10+<commit>") which reflects
        // the NuGet package version. Fall back to FileVersion if not set.
        var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var rawVersion = infoAttr?.InformationalVersion ?? FileVersionInfo.GetVersionInfo(asm.Location).FileVersion;

        rawVersion.Should().NotBeNullOrEmpty(because: $"{assemblyName} must carry version metadata");

        // Trim any +<commit> suffix before parsing
        var trimmed = rawVersion!.Split('+')[0].Trim();
        Version.TryParse(trimmed, out var parsed).Should().BeTrue(
            because: $"{assemblyName} informational/file version '{trimmed}' must be parseable as a Version");

        parsed!.Should().BeGreaterThanOrEqualTo(MinVersion,
            because: $"issue #202: {assemblyName} must not be downgraded below {MinVersion}; " +
                     "transitive deps require >= 10.0.10 (NU1605 warnings-as-errors)");
    }
}
