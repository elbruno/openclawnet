using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenClawNet.Storage;

namespace OpenClawNet.Storage.Azure;

public sealed class AppInsightsAuditSink : ISecretAccessAuditor
{
    private readonly ISecretAccessAuditor _inner;
    private readonly IVaultTelemetryClient _telemetry;

    public AppInsightsAuditSink(ISecretAccessAuditor inner, IVaultTelemetryClient telemetry)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task RecordAsync(string secretName, VaultCallerContext ctx, bool success, CancellationToken ct = default)
    {
        await _inner.RecordAsync(secretName, ctx, success, ct).ConfigureAwait(false);

        var telemetry = new EventTelemetry("VaultSecretAccess");
        telemetry.Properties["CallerType"] = ctx.CallerType.ToString();
        telemetry.Properties["CallerId"] = ctx.CallerId;
        telemetry.Properties["SessionId"] = ctx.SessionId ?? string.Empty;
        telemetry.Properties["Success"] = success.ToString();
        telemetry.Properties["Timestamp"] = DateTimeOffset.UtcNow.ToString("O");

        _telemetry.TrackEvent(telemetry);
    }
}

public interface IVaultTelemetryClient
{
    void TrackEvent(EventTelemetry telemetry);
}

public sealed class AppInsightsTelemetryClient : IVaultTelemetryClient
{
    private readonly TelemetryClient _telemetryClient;

    public AppInsightsTelemetryClient(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
    }

    public void TrackEvent(EventTelemetry telemetry) => _telemetryClient.TrackEvent(telemetry);
}

public static class AppInsightsAuditExtensions
{
    public static IServiceCollection AddAppInsightsVaultAudit(this IServiceCollection services)
    {
        services.TryAddSingleton<TelemetryClient>();
        services.TryAddSingleton<IVaultTelemetryClient, AppInsightsTelemetryClient>();
        services.AddScoped<ISecretAccessAuditor>(sp =>
        {
            var inner = sp.GetRequiredService<SecretAccessAuditor>();
            var telemetry = sp.GetRequiredService<IVaultTelemetryClient>();
            return new AppInsightsAuditSink(inner, telemetry);
        });

        return services;
    }
}
