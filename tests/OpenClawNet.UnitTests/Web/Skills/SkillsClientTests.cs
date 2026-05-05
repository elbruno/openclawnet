using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using OpenClawNet.Web.Models.Skills;
using OpenClawNet.Web.Services;

namespace OpenClawNet.UnitTests.Web.Skills;

public class SkillsClientTests
{
    private static (SkillsClient client, Mock<HttpMessageHandler> handler) CreateClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://gateway/") };
        return (new SkillsClient(http), handler);
    }

    [Fact]
    public async Task GetSnapshotAsync_DeserializesShape()
    {
        var (client, handler) = CreateClient();
        var snap = new SkillsSnapshotDto("01JC4ZD8X", DateTimeOffset.UtcNow, "+1 / -0");
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(snap) });

        var result = await client.GetSnapshotAsync();
        result.Id.Should().Be("01JC4ZD8X");
        result.ChangeSummary.Should().Be("+1 / -0");
    }

    [Fact]
    public async Task ListAsync_HitsCorrectUrl()
    {
        var (client, handler) = CreateClient();
        HttpRequestMessage? captured = null;
        var dto = new SkillDto("doc-processor", "Convert PDFs", "1.0", "system", null, "built-in", null,
            "sha256:abc", DateTimeOffset.UtcNow, "system", new() { ["chat-bot"] = true });

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { dto }) });

        var result = await client.ListAsync();
        result.Should().HaveCount(1);
        captured!.RequestUri!.AbsolutePath.Should().Be("/api/skills");
    }

    [Fact]
    public async Task CreateAsync_PostsJson()
    {
        var (client, handler) = CreateClient();
        HttpRequestMessage? captured = null;
        var created = new SkillDto("git-tutor", "help", "1.0", "installed", null, "manual", null,
            "sha256:7a", DateTimeOffset.UtcNow, "installed", new() { ["chat-bot"] = false });
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(created) });

        var dto = await client.CreateAsync(new CreateSkillRequest("git-tutor", "help", "1.0", "installed", null, new[] { "git" }, "# body"));
        dto.Name.Should().Be("git-tutor");
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/skills");
        var body = await captured.Content!.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"git-tutor\"");
    }

    [Fact]
    public async Task CreateAsync_OnBadRequest_ThrowsWithStructuredReason()
    {
        var (client, handler) = CreateClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new SkillsProblem("InvalidSkillName", "leading hyphen"))
            });

        var act = () => client.CreateAsync(new CreateSkillRequest("-bad", "x", null, "installed", null, null, "body"));
        var ex = await act.Should().ThrowAsync<SkillsClientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Which.Reason.Should().Be("InvalidSkillName");
    }

    [Fact]
    public async Task SetEnabledAsync_PutsToCorrectUrl_WithEnabledFlag()
    {
        var (client, handler) = CreateClient();
        HttpRequestMessage? captured = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.SetEnabledAsync("git-tutor", "chat-bot", enabled: true);
        captured!.Method.Should().Be(HttpMethod.Put);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/skills/git-tutor/enabled-for/chat-bot");
        var body = await captured.Content!.ReadAsStringAsync();
        body.Should().Contain("\"enabled\":true");
    }

    [Fact]
    public async Task DeleteAsync_HitsCorrectUrl()
    {
        var (client, handler) = CreateClient();
        HttpRequestMessage? captured = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.DeleteAsync("git-tutor");
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/skills/git-tutor");
    }

    [Fact]
    public async Task GetChangesSinceAsync_DeserializesDiff()
    {
        var (client, handler) = CreateClient();
        var diff = new SkillsChangesDto("prev", "next", new[] { "git-tutor" }, Array.Empty<string>(), Array.Empty<string>());
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(diff) });

        var result = await client.GetChangesSinceAsync("prev");
        result.Added.Should().Equal(new[] { "git-tutor" });
        result.CurrentSnapshotId.Should().Be("next");
    }

    [Fact]
    public async Task ErrorWithNonJsonBody_StillThrows_WithStatusCodeReason()
    {
        var (client, handler) = CreateClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });

        var act = () => client.ListAsync();
        var ex = await act.Should().ThrowAsync<SkillsClientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
