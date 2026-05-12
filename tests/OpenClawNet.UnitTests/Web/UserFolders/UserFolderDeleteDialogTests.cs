using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using OpenClawNet.Web.Components.UserFolders;
using OpenClawNet.Web.Models.UserFolders;
using OpenClawNet.Web.Services;

namespace OpenClawNet.UnitTests.Web.UserFolders;

/// <summary>
/// Drummond W-4 P0 #3 — the destructive-op confirm dialog MUST keep the
/// Submit button disabled until the user types the EXACT folder name. This
/// test exists to fail loudly if anyone weakens that comparison (e.g. trims,
/// lowercases, or treats empty as a wildcard).
/// </summary>
public class UserFolderDeleteDialogTests : TestContext, IDisposable
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly UserFolderClient _client;

    public UserFolderDeleteDialogTests()
    {
        var http = new HttpClient(_handler.Object) { BaseAddress = new Uri("http://gateway/") };
        _client = new UserFolderClient(http);
        Services.AddSingleton(_client);
    }

    [Fact]
    public void DeleteButton_DisabledByDefault_WhenInputIsEmpty()
    {
        var cut = Render<UserFolderDeleteDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.FolderName, "samples")
            .Add(x => x.Client, _client));

        var button = cut.Find("[data-testid='delete-confirm']");
        button.HasAttribute("disabled").Should().BeTrue();
    }

    [Theory]
    [InlineData("Samples")]   // case mismatch
    [InlineData("samples ")]   // trailing space
    [InlineData("sample")]     // partial
    [InlineData("samplesX")]   // extra char
    public void DeleteButton_StaysDisabled_WhenTypedDoesNotMatchExactly(string typed)
    {
        var cut = Render<UserFolderDeleteDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.FolderName, "samples")
            .Add(x => x.Client, _client));

        cut.Find("input[type='text']").Input(typed);

        cut.Find("[data-testid='delete-confirm']").HasAttribute("disabled").Should().BeTrue(
            because: $"'{typed}' is not an ordinal exact match for the folder name");
    }

    [Fact]
    public void DeleteButton_Enables_OnExactMatch()
    {
        var cut = Render<UserFolderDeleteDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.FolderName, "samples")
            .Add(x => x.Client, _client));

        cut.Find("input[type='text']").Input("samples");

        cut.Find("[data-testid='delete-confirm']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Submit_SendsDelete_WithConfirmHeader_AndFiresOnDeleted()
    {
        HttpRequestMessage? captured = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        string? deletedName = null;
        var cut = Render<UserFolderDeleteDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.FolderName, "samples")
            .Add(x => x.Client, _client)
            .Add(x => x.OnDeleted, name => { deletedName = name; }));

        cut.Find("input[type='text']").Input("samples");
        await cut.Find("[data-testid='delete-confirm']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.Headers.GetValues(UserFolderClient.ConfirmHeader).Single().Should().Be("samples");
        deletedName.Should().Be("samples");
    }

    [Fact]
    public void Submit_OnServerError_ShowsAlertReason()
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new UserFolderProblem("ConfirmationRequired"))
            });

        var cut = Render<UserFolderDeleteDialog>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.FolderName, "samples")
            .Add(x => x.Client, _client));

        cut.Find("input[type='text']").Input("samples");
        cut.Find("[data-testid='delete-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".alert-danger").TextContent.Should().Contain("ConfirmationRequired");
        });
    }
}
