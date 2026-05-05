// These tests deliberately exercise the obsolete ChatHub for back-compat regression coverage.
#pragma warning disable CS0618
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClawNet.Gateway.Hubs;

namespace OpenClawNet.IntegrationTests;

/// <summary>
/// Regression tests for the ChatHub SignalR contract.
///
/// Root cause of a prior bug: SignalR does NOT honour C# default parameter values.
/// Every parameter on a hub method must be explicitly sent by the client,
/// even optional ones. These tests lock in that contract so the mismatch
/// can never silently resurface.
/// </summary>
public sealed class ChatHubContractTests
{
    /// <summary>
    /// Verifies that StreamChat has exactly 3 parameters so that any future
    /// signature change is immediately caught ΓÇö no server or gateway required.
    /// </summary>
    [Fact]
    public void StreamChat_HubMethod_HasExactlyThreeParameters()
    {
        var method = typeof(ChatHub).GetMethod("StreamChat");

        method.Should().NotBeNull("StreamChat must be a public method on ChatHub");
        method!.GetParameters().Should().HaveCount(3,
            "client invokes StreamChat with 3 args (sessionId, message, model); " +
            "SignalR does not honour C# defaults so the count must match exactly");
    }

    [Fact]
    public void StreamChat_ThirdParameter_IsNullableString()
    {
        var method = typeof(ChatHub).GetMethod("StreamChat")!;
        var thirdParam = method.GetParameters()[2];

        thirdParam.Name.Should().Be("model");
        thirdParam.ParameterType.Should().Be(typeof(string),
            "model param should be string (nullable via NRT annotation)");
        thirdParam.IsOptional.Should().BeTrue(
            "model has a default value so C# marks it optional ΓÇö " +
            "but SignalR still requires the client to send it explicitly");
    }
}

/// <summary>
/// End-to-end streaming test using the real SignalR client against the in-memory test server.
/// Verifies the hub can be invoked with all 3 arguments and returns streaming events.
/// </summary>
public sealed class ChatHubStreamTests(GatewayWebAppFactory factory) : IClassFixture<GatewayWebAppFactory>, IAsyncLifetime
{
    private HubConnection? _connection;

    public async Task InitializeAsync()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{factory.Server.BaseAddress}hubs/chat", opts =>
            {
                // Route traffic through the in-memory test server ΓÇö no real network needed
                opts.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        await _connection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    [Fact]
    public async Task StreamChat_WithThreeArgs_DoesNotThrowArgumentCountError()
    {
        var sessionId = Guid.NewGuid();
        var events = new List<ChatHubMessage>();

        // Passing all 3 args explicitly (including model = null) ΓÇö this is the regression guard
        var stream = _connection!.StreamAsync<ChatHubMessage>(
            "StreamChat", sessionId, "Hello", (string?)null);

        Func<Task> act = async () =>
        {
            await foreach (var msg in stream)
            {
                events.Add(msg);
                if (msg.Type is "complete" or "error") break;
            }
        };

        await act.Should().NotThrowAsync(
            "invoking StreamChat with 3 args must not raise an argument-count mismatch");
    }

    [Fact]
    public async Task StreamChat_ReturnsContentEvents_FromFakeModel()
    {
        var sessionId = Guid.NewGuid();
        var events = new List<ChatHubMessage>();

        var stream = _connection!.StreamAsync<ChatHubMessage>(
            "StreamChat", sessionId, "Hi", (string?)null);

        await foreach (var msg in stream)
        {
            events.Add(msg);
            if (msg.Type is "complete" or "error") break;
        }

        events.Should().NotBeEmpty("the hub should yield at least one event");
        events.Should().Contain(m => m.Type == "content" || m.Type == "complete",
            "streaming must produce content or complete events");
    }

    [Fact]
    public async Task StreamChat_CompleteEvent_IsAlwaysLastEvent()
    {
        var sessionId = Guid.NewGuid();
        var events = new List<ChatHubMessage>();

        var stream = _connection!.StreamAsync<ChatHubMessage>(
            "StreamChat", sessionId, "ping", (string?)null);

        await foreach (var msg in stream)
        {
            events.Add(msg);
            if (msg.Type is "complete" or "error") break;
        }

        var lastEvent = events.Last();
        lastEvent.Type.Should().BeOneOf("complete", "error",
            "the final streaming event must always be 'complete' or 'error'");
    }
}
