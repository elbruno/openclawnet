using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Moq;
using Moq.Protected;
using MudBlazor;
using OpenClawNet.UnitTests.TestSupport;
using SessionsPage = OpenClawNet.Web.Components.Pages.Sessions;

namespace OpenClawNet.UnitTests.Web.Sessions;

public class SessionsDeleteConfirmationTests : MudBlazorTestContext, IDisposable
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly HttpClient _client;

    public SessionsDeleteConfirmationTests()
    {
        _client = new HttpClient(_handler.Object)
        {
            BaseAddress = new Uri("http://gateway/")
        };

        _httpClientFactory.Setup(f => f.CreateClient("gateway")).Returns(_client);
        Services.AddSingleton(_httpClientFactory.Object);

        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public async Task SingleDelete_OpensConfirmation_BeforeIssuingDelete()
    {
        var sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SetupSessionsListResponse((sessionId, "Single delete test"));

        HttpRequestMessage? capturedDelete = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == $"/api/sessions/{sessionId}"),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedDelete = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderComponent<SessionsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll($"[data-testid='session-row-{sessionId}']").Should().NotBeEmpty();
        });

        cut.Find($"[data-testid='session-delete-{sessionId}']").Click();

        cut.FindAll("[data-testid='session-delete-dialog']").Should().NotBeEmpty();
        capturedDelete.Should().BeNull("delete should not fire until the user confirms");

        cut.Find("[data-testid='session-delete-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            capturedDelete.Should().NotBeNull();
            cut.FindAll($"[data-testid='session-row-{sessionId}']").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task BulkDelete_OpensConfirmation_BeforeIssuingBulkDelete()
    {
        var sessionA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var sessionB = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SetupSessionsListResponse((sessionA, "Bulk delete A"), (sessionB, "Bulk delete B"));

        HttpRequestMessage? capturedDelete = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath == "/api/sessions"),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedDelete = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var cut = RenderComponent<SessionsPage>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll($"[data-testid='session-row-{sessionA}']").Should().NotBeEmpty();
            cut.FindAll($"[data-testid='session-row-{sessionB}']").Should().NotBeEmpty();
        });

        cut.Find($"[data-testid='session-select-{sessionA}']").Change(true);
        cut.Find($"[data-testid='session-select-{sessionB}']").Change(true);
        cut.Find("[data-testid='sessions-delete-selected']").Click();

        cut.FindAll("[data-testid='session-delete-dialog']").Should().NotBeEmpty();
        cut.Find("[data-testid='session-delete-title']").TextContent.Should().Contain("Delete 2 sessions");
        capturedDelete.Should().BeNull("bulk delete should not fire until the user confirms");

        cut.Find("[data-testid='session-delete-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            capturedDelete.Should().NotBeNull();
            cut.FindAll($"[data-testid='session-row-{sessionA}']").Should().BeEmpty();
            cut.FindAll($"[data-testid='session-row-{sessionB}']").Should().BeEmpty();
        });
    }

    private void SetupSessionsListResponse(params (Guid id, string title)[] sessions)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/sessions"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(sessions.Select(session => new
                {
                    id = session.id,
                    title = session.title,
                    createdAt = DateTime.UtcNow,
                    updatedAt = DateTime.UtcNow
                }).ToList())
            });
    }
}
