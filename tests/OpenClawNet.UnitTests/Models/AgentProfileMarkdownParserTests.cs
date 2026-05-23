using FluentAssertions;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.UnitTests.Models;

/// <summary>
/// Tests for <see cref="AgentProfileMarkdownParser"/> — the Markdown-to-AgentProfile parser
/// added in Phase 1.
/// </summary>
public class AgentProfileMarkdownParserTests
{
    [Fact]
    public void Parse_PlainMarkdownWithHeading_SetsNameFromSlugifiedHeading()
    {
        var markdown = "# My Custom Agent\nSome instructions here.";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Name.Should().Be("my-custom-agent");
        profile.Instructions.Should().Be("# My Custom Agent\nSome instructions here.");
    }

    [Fact]
    public void Parse_PlainMarkdownWithHeading_PreservesFullContentAsInstructions()
    {
        var markdown = "# Helper Bot\n\nYou are a helpful bot.\n\n## Details\n\nMore info.";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Instructions.Should().Be(markdown);
    }

    [Fact]
    public void Parse_YamlFrontMatter_AllFieldsParsed()
    {
        var markdown = """
            ---
            name: my-agent
            displayName: My Agent
            provider: azure-openai
            model: gpt-4o
            tools: [tool1, tool2, tool3]
            temperature: 0.7
            maxTokens: 4096
            retrievalLevel: vectorDb
            ---
            You are a specialized agent.
            """;

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Name.Should().Be("my-agent");
        profile.DisplayName.Should().Be("My Agent");
        profile.Provider.Should().Be("azure-openai");
        // PR-F: AgentProfile no longer has a Model field — front-matter `model:` is ignored.
        profile.EnabledTools.Should().Be("tool1,tool2,tool3");
        profile.Temperature.Should().Be(0.7);
        profile.MaxTokens.Should().Be(4096);
        profile.RetrievalLevel.Should().Be(RetrievalLevel.VectorDb);
        profile.Instructions.Should().Be("You are a specialized agent.");
    }

    [Fact]
    public void Parse_FrontMatterWithoutName_FallsBackToHeading()
    {
        var markdown = "---\nprovider: ollama\nmodel: llama3\n---\n# Research Assistant\nDo research.";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Name.Should().Be("research-assistant");
        profile.Provider.Should().Be("ollama");
        // PR-F: front-matter `model:` is ignored — the agent's model now comes from the provider.
    }

    [Fact]
    public void Parse_FrontMatterNoNameNoHeading_FallsBackToFallbackName()
    {
        var markdown = "---\nprovider: ollama\n---\nJust some instructions without a heading.";

        var profile = AgentProfileMarkdownParser.Parse(markdown, fallbackName: "my-fallback");

        profile.Name.Should().Be("my-fallback");
    }

    [Fact]
    public void Parse_EmptyContent_UsesFallbackName()
    {
        var profile = AgentProfileMarkdownParser.Parse("", fallbackName: "empty-fallback");

        profile.Name.Should().Be("empty-fallback");
        profile.Instructions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceOnly_UsesFallbackName()
    {
        var profile = AgentProfileMarkdownParser.Parse("   \n  \n  ", fallbackName: "ws-fallback");

        profile.Name.Should().Be("ws-fallback");
    }

    [Fact]
    public void Parse_EmptyContentNoFallback_GeneratesTimestampName()
    {
        var profile = AgentProfileMarkdownParser.Parse("");

        profile.Name.Should().StartWith("imported-");
    }

    [Fact]
    public void Parse_ToolsAsArray_JoinsWithComma()
    {
        var markdown = "---\nname: tool-test\ntools: [tool1, tool2, tool3]\n---\nInstructions.";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.EnabledTools.Should().Be("tool1,tool2,tool3");
    }

    [Fact]
    public void Parse_MalformedYaml_NoClosingDelimiter_TreatsEntireContentAsInstructions()
    {
        var markdown = "---\nname: broken\nprovider: ollama\nSome instructions here";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        // No closing --- means HasFrontMatter returns false; entire string is body
        profile.Instructions.Should().Be(markdown.Trim());
        // Name should be derived from heading or fallback, not from front-matter
        profile.Name.Should().NotBe("broken");
    }

    [Fact]
    public void Parse_FrontMatterWithPartialFields_UnsetFieldsAreNull()
    {
        var markdown = "---\nname: partial\nprovider: ollama\n---\nMinimal config.";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Name.Should().Be("partial");
        profile.Provider.Should().Be("ollama");
        profile.DisplayName.Should().BeNull();
        profile.EnabledTools.Should().BeNull();
        profile.Temperature.Should().BeNull();
        profile.MaxTokens.Should().BeNull();
    }

    [Fact]
    public void Parse_KindFrontMatter_ToolTester()
    {
        var markdown = """
            ---
            name: tool-tester
            kind: ToolTester
            ---
            Tester instructions.
            """;

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Kind.Should().Be(ProfileKind.ToolTester);
    }

    [Fact]
    public void Parse_KindFrontMatter_System_CaseInsensitive()
    {
        var markdown = """
            ---
            name: sys
            kind: system
            ---
            System instructions.
            """;

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Kind.Should().Be(ProfileKind.System);
    }

    [Fact]
    public void Parse_NoKindFrontMatter_DefaultsToStandard()
    {
        var markdown = """
            ---
            name: plain
            ---
            Hi.
            """;

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Kind.Should().Be(ProfileKind.Standard);
        profile.RetrievalLevel.Should().Be(RetrievalLevel.Off);
    }

    [Fact]
    public void Parse_NullMarkdown_ThrowsArgumentNullException()
    {
        var act = () => AgentProfileMarkdownParser.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("# Hello World", "hello-world")]
    [InlineData("# My  Agent!", "my-agent")]
    [InlineData("# Test---Agent", "test-agent")]
    public void Parse_HeadingSlugification_ProducesExpectedName(string heading, string expectedName)
    {
        var profile = AgentProfileMarkdownParser.Parse(heading + "\nBody.");

        profile.Name.Should().Be(expectedName);
    }

    [Fact]
    public void Parse_BodyAfterFrontMatter_IsTrimmed()
    {
        var markdown = "---\nname: trimtest\n---\n\n  Hello world  \n\n";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        profile.Instructions.Should().Be("Hello world");
    }

    [Fact]
    public void Parse_HeadingWithSpecialCharacters_SlugifiesCorrectly()
    {
        var markdown = "# My Agent (v2.0) — Enhanced!\nInstructions.";

        var profile = AgentProfileMarkdownParser.Parse(markdown);

        // Special chars stripped, spaces become hyphens
        profile.Name.Should().Be("my-agent-v20-enhanced");
    }
}
