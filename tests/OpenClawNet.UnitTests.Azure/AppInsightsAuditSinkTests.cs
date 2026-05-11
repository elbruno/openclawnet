using Microsoft.ApplicationInsights.DataContracts;
using Moq;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Azure;

namespace OpenClawNet.UnitTests.Azure;

public sealed class AppInsightsAuditSinkTests
{
    [Fact]
    public async Task RecordAsync_WritesInnerAuditAndTracksEvent()
    {
        var inner = new Mock<ISecretAccessAuditor>(MockBehavior.Strict);
        inner.Setup(a => a.RecordAsync("Secret", It.IsAny<VaultCallerContext>(), true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var telemetryClient = new TestTelemetryClient();
        var sink = new AppInsightsAuditSink(inner.Object, telemetryClient);

        var ctx = new VaultCallerContext(VaultCallerType.Tool, "caller", "session-1");
        await sink.RecordAsync("Secret", ctx, true);

        inner.Verify(a => a.RecordAsync("Secret", It.IsAny<VaultCallerContext>(), true, It.IsAny<CancellationToken>()), Times.Once);
        var evt = Assert.Single(telemetryClient.Events);
        Assert.Equal("VaultSecretAccess", evt.Name);
        Assert.Equal("Secret", evt.Properties["SecretName"]);
        Assert.Equal("Tool", evt.Properties["CallerType"]);
        Assert.Equal("caller", evt.Properties["CallerId"]);
        Assert.Equal("session-1", evt.Properties["SessionId"]);
        Assert.Equal("True", evt.Properties["Success"]);
        Assert.DoesNotContain("SecretValue", evt.Properties.Values, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordAsync_DoesNotEmitSecretValue()
    {
        var inner = new Mock<ISecretAccessAuditor>(MockBehavior.Strict);
        inner.Setup(a => a.RecordAsync("Secret", It.IsAny<VaultCallerContext>(), false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var telemetryClient = new TestTelemetryClient();
        var sink = new AppInsightsAuditSink(inner.Object, telemetryClient);

        await sink.RecordAsync("Secret", new VaultCallerContext(VaultCallerType.Configuration, "cfg"), false);

        var evt = Assert.Single(telemetryClient.Events);
        Assert.DoesNotContain("super-secret-value", evt.Properties.Values, StringComparer.Ordinal);
    }

    private sealed class TestTelemetryClient : IVaultTelemetryClient
    {
        public List<EventTelemetry> Events { get; } = new();

        public void TrackEvent(EventTelemetry telemetry) => Events.Add(telemetry);
    }
}
