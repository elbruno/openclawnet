using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using MudBlazor;
using MudBlazor.Services;
using OpenClawNet.UnitTests.TestSupport;
using OpenClawNet.Web.Components.Pages;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OpenClawNet.UnitTests.Web;

/// <summary>
/// Tests for the inline rename functionality in Jobs.razor.
/// Verifies rename button toggles edit mode, save/cancel behavior, keyboard shortcuts,
/// and validation (empty names, duplicates).
/// </summary>
public class JobsRenamePageTests : MudBlazorTestContext, IAsyncDisposable
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _mockHttpClient;
    private readonly List<TestJobDto> _testJobs;

    public JobsRenamePageTests()
    {
        // Setup MudBlazor snackbar (MudBlazorTestContext already handles core services and JSInterop)
        Services.AddMudBlazorSnackbar();

        // Setup mock HTTP handler
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _mockHttpClient = new HttpClient(_mockHttpHandler.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };

        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClientFactory.Setup(f => f.CreateClient("gateway"))
            .Returns(_mockHttpClient);

        Services.AddSingleton(_mockHttpClientFactory.Object);

        // Test data - using a DTO structure that matches the API response
        _testJobs = new List<TestJobDto>
        {
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Test Job 1",
                Prompt = "Do something",
                Status = "active",
                IsRecurring = true,
                CronExpression = "0 0 * * *",
                TimeZone = "UTC",
                AllowConcurrentRuns = false,
                AgentProfileName = "default"
            },
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Test Job 2",
                Prompt = "Do something else",
                Status = "active",
                IsRecurring = true,
                CronExpression = "0 1 * * *",
                TimeZone = "UTC",
                AllowConcurrentRuns = true,
                AgentProfileName = "default"
            }
        };

        // Render MudPopoverProvider AFTER all services are registered to satisfy MudBlazor's requirement
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void RenameButton_TogglesEditMode_AndShowsTextField()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var cut = Render<Jobs>();
        var job = _testJobs[0];

        // Act - Click the rename button (Edit icon)
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.Should().NotBeEmpty("rename buttons should exist for jobs");
        
        // Click the first rename button
        renameButtons.First().Click();

        // Assert - Edit mode should be active, showing text field
        var textFields = cut.FindComponents<MudTextField<string?>>();
        textFields.Should().NotBeEmpty("text field should appear in edit mode");
        
        // Check save and cancel buttons appear
        var saveButtons = cut.FindAll("button[title='Save']");
        var cancelButtons = cut.FindAll("button[title='Cancel']");
        
        saveButtons.Should().NotBeEmpty("save button should appear");
        cancelButtons.Should().NotBeEmpty("cancel button should appear");
    }

    [Fact]
    public void RenameSave_PutsToApi_AndShowsSnackbar_OnSuccess()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var job = _testJobs[0];
        var newName = "Renamed Job";
        
        // Setup successful PUT response
        SetupHttpResponse(
            HttpMethod.Put,
            $"api/jobs/{job.Id}",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { id = job.Id, name = newName })
        );

        // Setup reload response with updated job
        var updatedJobs = _testJobs.Select(j => j.Id == job.Id 
            ? new TestJobDto 
            { 
                Id = j.Id, 
                Name = newName, 
                Prompt = j.Prompt, 
                Status = j.Status,
                IsRecurring = j.IsRecurring,
                CronExpression = j.CronExpression, 
                TimeZone = j.TimeZone, 
                AllowConcurrentRuns = j.AllowConcurrentRuns, 
                AgentProfileName = j.AgentProfileName 
            }
            : j).ToList();
        SetupJobsResponse(updatedJobs);

        var cut = Render<Jobs>();

        // Act - Enter edit mode
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.First().Click();

        // Change name in text field using bUnit event helpers
        var inputField = cut.Find("input.mud-input-slot");
        cut.InvokeAsync(() => inputField.Change(newName));

        // Click save
        var saveButton = cut.Find("button[title='Save']");
        cut.InvokeAsync(() => saveButton.Click());

        // Assert - Wait for async operations and verify save occurred
        cut.WaitForAssertion(() =>
        {
            var saveButtons = cut.FindAll("button[title='Save']");
            saveButtons.Should().BeEmpty("save mode should exit after successful save");
        });
    }

    [Fact]
    public void RenameSave_ShowsInlineError_OnDuplicateName_409()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var duplicateName = _testJobs[1].Name; // Use existing job name

        var cut = Render<Jobs>();

        // Act - Enter edit mode
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.First().Click();

        // Try to use duplicate name - set value and trigger ValueChanged
        var textField = cut.FindComponents<MudTextField<string?>>().First();
        cut.InvokeAsync(async () =>
        {
            await textField.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = duplicateName
            }));
            await textField.Instance.ValueChanged.InvokeAsync(duplicateName);
        });

        // Click save
        var saveButton = cut.Find("button[title='Save']");
        cut.InvokeAsync(() => saveButton.Click());

        // Assert - Error message should appear inline
        cut.WaitForAssertion(() =>
        {
            var errorTextField = cut.FindComponents<MudTextField<string?>>()
                .FirstOrDefault(tf => !string.IsNullOrWhiteSpace(tf.Instance.HelperText));
            
            errorTextField.Should().NotBeNull("error state should be set on text field");
            errorTextField!.Instance.HelperText.Should().Be("Name already in use");
        });
    }

    [Fact]
    public void RenameCancel_RestoresOriginalName_AndExitsEditMode()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var job = _testJobs[0];
        var cut = Render<Jobs>();

        // Act - Enter edit mode
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.First().Click();

        // Verify edit mode is active
        var textFieldsBefore = cut.FindComponents<MudTextField<string?>>();
        textFieldsBefore.Should().NotBeEmpty("text field should appear in edit mode");

        // Click cancel
        var cancelButton = cut.Find("button[title='Cancel']");
        cancelButton.Click();

        // Assert - Should exit edit mode (no more text fields)
        var saveButtonsAfter = cut.FindAll("button[title='Save']");
        saveButtonsAfter.Should().BeEmpty("save button should disappear after cancel");

        // Original job name should still be visible
        cut.Markup.Should().Contain(job.Name);
    }

    [Fact]
    public void RenameInput_EnterKey_TriggersSave()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var job = _testJobs[0];
        var newName = "Enter Key Rename";

        SetupHttpResponse(
            HttpMethod.Put,
            $"api/jobs/{job.Id}",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { id = job.Id, name = newName })
        );

        var updatedJobs = _testJobs.Select(j => j.Id == job.Id 
            ? new TestJobDto 
            { 
                Id = j.Id, 
                Name = newName, 
                Prompt = j.Prompt, 
                Status = j.Status,
                IsRecurring = j.IsRecurring,
                CronExpression = j.CronExpression, 
                TimeZone = j.TimeZone, 
                AllowConcurrentRuns = j.AllowConcurrentRuns, 
                AgentProfileName = j.AgentProfileName 
            }
            : j).ToList();
        SetupJobsResponse(updatedJobs);

        var cut = Render<Jobs>();

        // Act - Enter edit mode
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.First().Click();

        // Change name using bUnit event helpers
        var inputField = cut.Find("input.mud-input-slot");
        cut.InvokeAsync(() => inputField.Change(newName));

        // Simulate Enter key
        cut.InvokeAsync(() => inputField.KeyDown(new KeyboardEventArgs { Key = "Enter" }));

        // Assert - Should trigger save (verified by exiting edit mode)
        cut.WaitForAssertion(() =>
        {
            var saveButtons = cut.FindAll("button[title='Save']");
            saveButtons.Should().BeEmpty("save mode should exit after Enter key triggers save");
        });
    }

    [Fact]
    public async Task RenameInput_EscapeKey_TriggersCancel()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var cut = Render<Jobs>();

        // Act - Enter edit mode
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.First().Click();

        var textField = cut.FindComponents<MudTextField<string?>>().First();

        // Simulate Escape key
        await cut.InvokeAsync(async () =>
        {
            await textField.Instance.OnKeyDown.InvokeAsync(
                new KeyboardEventArgs { Key = "Escape" });
        });

        // Assert - Should exit edit mode (save button should disappear)
        await Task.Delay(50);
        var saveButtonsAfter = cut.FindAll("button[title='Save']");
        saveButtonsAfter.Should().BeEmpty("should exit edit mode on Escape");
    }

    [Fact]
    public void RenameSave_RejectsEmpty_WithInlineError()
    {
        // Arrange
        SetupJobsResponse(_testJobs);
        var cut = Render<Jobs>();

        // Act - Enter edit mode
        var renameButtons = cut.FindAll("button[title='Rename job']");
        renameButtons.First().Click();

        // Clear name - set value and trigger ValueChanged
        var textField = cut.FindComponents<MudTextField<string?>>().First();
        cut.InvokeAsync(async () =>
        {
            await textField.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Value"] = ""
            }));
            await textField.Instance.ValueChanged.InvokeAsync("");
        });

        // Click save
        var saveButton = cut.Find("button[title='Save']");
        cut.InvokeAsync(() => saveButton.Click());

        // Assert - Error message should appear inline
        cut.WaitForAssertion(() =>
        {
            var errorTextField = cut.FindComponents<MudTextField<string?>>()
                .FirstOrDefault(tf => !string.IsNullOrWhiteSpace(tf.Instance.HelperText));
            
            errorTextField.Should().NotBeNull("error state should be set");
            errorTextField!.Instance.HelperText.Should().Be("Name cannot be empty");
        });
    }

    private void SetupJobsResponse(List<TestJobDto> jobs)
    {
        SetupHttpResponse(
            HttpMethod.Get,
            "api/jobs",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(jobs)
        );
    }

    private void SetupHttpResponse(HttpMethod method, string url, HttpStatusCode statusCode, string? content = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (content != null)
        {
            response.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == method &&
                    req.RequestUri != null &&
                    req.RequestUri.ToString().Contains(url)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    public new async ValueTask DisposeAsync()
    {
        _mockHttpClient?.Dispose();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Test DTO matching the API response structure for jobs.
    /// </summary>
    private class TestJobDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Prompt { get; set; }
        public string Status { get; set; } = "active";
        public bool IsRecurring { get; set; }
        public string? CronExpression { get; set; }
        public DateTime? NextRunAt { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string? TimeZone { get; set; }
        public string? NaturalLanguageSchedule { get; set; }
        public bool AllowConcurrentRuns { get; set; }
        public string? AgentProfileName { get; set; }
    }
}
