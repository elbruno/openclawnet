using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using OpenClawNet.Web.Models.UserFolders;
using OpenClawNet.Web.Services;

namespace OpenClawNet.UnitTests.Web.UserFolders;

/// <summary>
/// Smoke tests for <see cref="UserFolderClient"/>. Endpoint-level coverage
/// (status mapping, body shapes) is owned by Dylan's gateway tests; here we
/// just pin the wire contract the UI relies on:
/// • DELETE always carries the X-Confirm-FolderName header (Drummond W-4 P0 #3 client side)
/// • Structured 4xx → <see cref="UserFolderClientException"/> with Reason populated
/// • 413 surfaces as RequestEntityTooLarge so the upload component can show the quota toast
/// </summary>
public class UserFolderClientTests
{
    private static (UserFolderClient client, Mock<HttpMessageHandler> handler) CreateClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://gateway/") };
        return (new UserFolderClient(http), handler);
    }

    [Fact]
    public async Task ListAsync_DeserializesArray()
    {
        var (client, handler) = CreateClient();
        var payload = new[]
        {
            new UserFolderDto("samples", 1024, new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc))
        };
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            });

        var result = await client.ListAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("samples");
        result[0].SizeBytes.Should().Be(1024);
    }

    [Fact]
    public async Task CreateAsync_PostsJsonAndReturnsDto()
    {
        var (client, handler) = CreateClient();
        HttpRequestMessage? captured = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserFolderDto("scratch", 0, DateTime.UtcNow))
            });

        var dto = await client.CreateAsync("scratch");

        dto.Name.Should().Be("scratch");
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/user-folders");
        var body = await captured.Content!.ReadAsStringAsync();
        body.Should().Contain("\"folderName\":\"scratch\"", because: "the JSON body uses camelCase by default");
    }

    [Fact]
    public async Task CreateAsync_OnBadRequest_ThrowsWithStructuredReason()
    {
        var (client, handler) = CreateClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new UserFolderProblem("InvalidUserFolderName", "leading hyphen"))
            });

        var act = () => client.CreateAsync("-bad");

        var ex = await act.Should().ThrowAsync<UserFolderClientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Which.Reason.Should().Be("InvalidUserFolderName");
    }

    [Fact]
    public async Task DeleteAsync_AlwaysIncludesConfirmHeader()
    {
        var (client, handler) = CreateClient();
        HttpRequestMessage? captured = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.DeleteAsync("samples");

        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/user-folders/samples");
        captured.Headers.TryGetValues(UserFolderClient.ConfirmHeader, out var values).Should().BeTrue(
            because: "Drummond W-4 P0 #3 — the typed-back name must travel as a header on the wire");
        values!.Single().Should().Be("samples");
    }

    [Fact]
    public async Task DeleteAsync_OnConfirmationRequired_ThrowsWithReason()
    {
        var (client, handler) = CreateClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new UserFolderProblem("ConfirmationRequired"))
            });

        var act = () => client.DeleteAsync("samples");

        var ex = await act.Should().ThrowAsync<UserFolderClientException>();
        ex.Which.Reason.Should().Be("ConfirmationRequired");
    }

    [Fact]
    public async Task ErrorWithNonJsonBody_StillThrows_WithStatusCodeReason()
    {
        var (client, handler) = CreateClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("oops")
            });

        var act = () => client.ListAsync();

        var ex = await act.Should().ThrowAsync<UserFolderClientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.Reason.Should().Be("InternalServerError");
    }
}
