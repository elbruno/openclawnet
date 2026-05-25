using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenClawNet.PlaywrightDemoLauncher;

internal sealed class PlaywrightDemoCatalog
{
    private PlaywrightDemoCatalog(List<PlaywrightDemoSuite> suites, List<PlaywrightDemoTest> tests)
    {
        Suites = suites;
        Tests = tests;
    }

    public IReadOnlyList<PlaywrightDemoSuite> Suites { get; }

    public IReadOnlyList<PlaywrightDemoTest> Tests { get; }

    public static PlaywrightDemoCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find test catalog at '{path}'.", path);
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        using var reader = File.OpenText(path);
        var document = deserializer.Deserialize<PlaywrightDemoCatalogDocument>(reader)
            ?? throw new InvalidOperationException($"Catalog '{path}' could not be read.");

        return new PlaywrightDemoCatalog(document.Suites, document.Tests);
    }

    private sealed class PlaywrightDemoCatalogDocument
    {
        public List<PlaywrightDemoSuite> Suites { get; set; } = [];

        public List<PlaywrightDemoTest> Tests { get; set; } = [];
    }
}

internal sealed class PlaywrightDemoSuite
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string? Filter { get; set; }

    public bool AspireRequired { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

internal sealed class PlaywrightDemoTest
{
    public string Id { get; set; } = string.Empty;

    public string Suite { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string? MethodName { get; set; }

    public string Proves { get; set; } = string.Empty;

    public List<string> Category { get; set; } = [];

    public string? IssueRef { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(MethodName)
        ? ClassName
        : $"{ClassName}.{MethodName}";
}
