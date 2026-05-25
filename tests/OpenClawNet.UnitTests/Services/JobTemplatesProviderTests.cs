using OpenClawNet.Gateway.Services.JobTemplates;
using Xunit;

namespace OpenClawNet.UnitTests.Services;

public sealed class JobTemplatesProviderTests
{
    [Fact]
    public void GetAll_LoadsAllBuiltInTemplates()
    {
        var provider = new JobTemplatesProvider();
        var all = provider.GetAll();

        // We seed 6 demos as built-in templates.
        Assert.Equal(6, all.Count);
        Assert.All(all, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id), $"Template missing id");
            Assert.False(string.IsNullOrWhiteSpace(t.Name), $"Template '{t.Id}' missing Name");
            Assert.False(string.IsNullOrWhiteSpace(t.Description), $"Template '{t.Id}' missing Description");
            Assert.NotNull(t.DefaultJob);
            Assert.False(string.IsNullOrWhiteSpace(t.DefaultJob.Name), $"Template '{t.Id}' DefaultJob missing Name");
            Assert.False(string.IsNullOrWhiteSpace(t.DefaultJob.Prompt), $"Template '{t.Id}' DefaultJob missing Prompt");
        });
    }

    [Theory]
    [InlineData("watched-folder-summarizer")]
    [InlineData("github-issue-triage")]
    [InlineData("research-and-archive")]
    [InlineData("image-batch-resize")]
    [InlineData("text-to-speech-snippet")]
    [InlineData("rss-daily-summary")]
    public void Get_ReturnsKnownTemplate(string id)
    {
        var provider = new JobTemplatesProvider();
        var template = provider.Get(id);
        Assert.NotNull(template);
        Assert.Equal(id, template!.Id);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownId()
    {
        var provider = new JobTemplatesProvider();
        Assert.Null(provider.Get("does-not-exist"));
    }

    [Fact]
    public void WatchedFolderTemplate_HasCronExpression()
    {
        // Smoke test that the headline demo template stays scheduled (matches the docs).
        var provider = new JobTemplatesProvider();
        var template = provider.Get("watched-folder-summarizer")!;
        Assert.Equal("*/5 * * * *", template.DefaultJob.CronExpression);
        Assert.Contains("file_system", template.RequiredTools);
        Assert.Contains("markdown_convert", template.RequiredTools);
    }

    [Fact]
    public void GitHubTriageTemplate_DeclaresGitHubTokenSecret()
    {
        // Templates surface required secrets so the UI can warn before instantiation.
        var provider = new JobTemplatesProvider();
        var template = provider.Get("github-issue-triage")!;
        Assert.Contains("GITHUB_TOKEN", template.RequiredSecrets);
    }

    [Fact]
    public void RssDailySummaryTemplate_HasExpectedCronAndTools()
    {
        var provider = new JobTemplatesProvider();
        var template = provider.Get("rss-daily-summary")!;

        Assert.Equal("0 9 * * *", template.DefaultJob.CronExpression);
        Assert.Contains("web_fetch", template.RequiredTools);
        Assert.Contains("file_system", template.RequiredTools);
        Assert.Contains("notienenombre.com", template.DefaultJob.Prompt, StringComparison.OrdinalIgnoreCase);
    }
}
