using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Gateway.Endpoints;
using OpenClawNet.Gateway.Services.JobTemplates;

namespace OpenClawNet.IntegrationTests.Demos;

/// <summary>
/// End-to-end coverage for the 5 built-in job templates that back the
/// <c>docs/demos/tools/</c> walkthroughs.
///
/// These tests exercise the **HTTP plumbing** in process, but use the in-memory
/// fake model client (see <see cref="GatewayWebAppFactory"/>). They prove that:
///   1. Every shipped JSON template loads successfully at gateway startup.
///   2. Each template's <see cref="JobTemplate.DefaultJob"/> is structurally
///      valid — i.e. the API accepts it without a 400 response and persists a
///      real <c>ScheduledJob</c> row with the expected fields.
///   3. The <c>/api/jobs/{id}/runs/{runId}/events</c> endpoint returns the
///      expected 404-on-unknown-run shape.
///
/// They do **not** invoke a real LLM — that is covered by the live demo smoke
/// test (gated on <c>[Trait("Category", "Live")]</c>) once one is added.
///
/// Tagged <c>Trait("Category", "Integration")</c> following the existing scheme.
/// </summary>
[Trait("Category", "Integration")]
public sealed class JobTemplatesE2ETests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>The 6 templates we ship today. If a new one is added, update this list.</summary>
    public static IEnumerable<object[]> AllTemplateIds() =>
    [
        ["watched-folder-summarizer"],
        ["github-issue-triage"],
        ["research-and-archive"],
        ["image-batch-resize"],
        ["text-to-speech-snippet"],
        ["rss-daily-summary"]
    ];

    [Fact]
    public async Task ListTemplates_ReturnsAllBuiltInTemplates()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/jobs/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await response.Content.ReadFromJsonAsync<List<JobTemplate>>(JsonOpts);
        templates.Should().NotBeNull();
        templates!.Select(t => t.Id).Should().BeEquivalentTo(
            "watched-folder-summarizer",
            "github-issue-triage",
            "research-and-archive",
            "image-batch-resize",
            "text-to-speech-snippet",
            "rss-daily-summary");

        // Every template must have the fields the UI relies on.
        foreach (var t in templates)
        {
            t.Name.Should().NotBeNullOrWhiteSpace();
            t.Description.Should().NotBeNullOrWhiteSpace();
            t.DefaultJob.Should().NotBeNull();
            t.DefaultJob.Name.Should().NotBeNullOrWhiteSpace();
            t.DefaultJob.Prompt.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [MemberData(nameof(AllTemplateIds))]
    public async Task GetTemplate_ById_ReturnsTemplate(string templateId)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/jobs/templates/{templateId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var template = await response.Content.ReadFromJsonAsync<JobTemplate>(JsonOpts);
        template.Should().NotBeNull();
        template!.Id.Should().Be(templateId);
    }

    [Fact]
    public async Task GetTemplate_UnknownId_Returns404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/jobs/templates/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The contract the templates UI relies on: take a template's <c>defaultJob</c>
    /// payload and POST it straight to <c>/api/jobs</c>. Every shipped template
    /// must produce a successfully-created job.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTemplateIds))]
    public async Task PostingTemplateDefaultJob_CreatesScheduledJob(string templateId)
    {
        var client = factory.CreateClient();

        var template = await client.GetFromJsonAsync<JobTemplate>(
            $"/api/jobs/templates/{templateId}", JsonOpts);
        template.Should().NotBeNull();

        var createResponse = await client.PostAsJsonAsync("/api/jobs", template!.DefaultJob, JsonOpts);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            $"the gateway must accept template '{templateId}' as a valid CreateJobRequest");

        var created = await createResponse.Content.ReadFromJsonAsync<CreatedJobShape>(JsonOpts);
        created.Should().NotBeNull();
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be(template.DefaultJob.Name);
        created.Prompt.Should().Be(template.DefaultJob.Prompt);
        if (!string.IsNullOrEmpty(template.DefaultJob.CronExpression))
        {
            created.IsRecurring.Should().BeTrue("templates with a cron expression should produce recurring jobs");
            created.CronExpression.Should().Be(template.DefaultJob.CronExpression);
        }
    }

    [Fact]
    public async Task GetEvents_UnknownRun_Returns404()
    {
        var client = factory.CreateClient();

        // First create a real job from a template, then ask for events of a non-existent run id.
        var template = await client.GetFromJsonAsync<JobTemplate>(
            "/api/jobs/templates/watched-folder-summarizer", JsonOpts);
        var createResponse = await client.PostAsJsonAsync("/api/jobs", template!.DefaultJob, JsonOpts);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedJobShape>(JsonOpts);

        var response = await client.GetAsync(
            $"/api/jobs/{created!.Id}/runs/{Guid.NewGuid()}/events");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "asking for events of a run that doesn't exist on this job must be a clean 404");
    }

    [Fact]
    public async Task RssDailySummaryTemplate_SeedsDailyNineAmJob()
    {
        var client = factory.CreateClient();

        var template = await client.GetFromJsonAsync<JobTemplate>(
            "/api/jobs/templates/rss-daily-summary", JsonOpts);
        template.Should().NotBeNull();

        var createResponse = await client.PostAsJsonAsync("/api/jobs", template!.DefaultJob, JsonOpts);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedJobShape>(JsonOpts);
        created.Should().NotBeNull();
        created!.IsRecurring.Should().BeTrue();
        created.CronExpression.Should().Be("0 9 * * *");
        created.Prompt.Should().ContainEquivalentOf("notienenombre.com");
    }

    /// <summary>Minimal shape we read back from POST /api/jobs.</summary>
    private sealed record CreatedJobShape(
        Guid Id,
        string Name,
        string Prompt,
        string Status,
        bool IsRecurring,
        string? CronExpression);
}
