using Xunit;

namespace OpenClawNet.PlaywrightTests.Demos;

[Collection("AspireHost")]
[Trait("Category", "DemoLive")]
public sealed class AspireHostFixturePilotTests
{
    private readonly AspireHostFixture _fixture;

    public AspireHostFixturePilotTests(AspireHostFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task AspireHostFixture_AttachesOrStarts_AndExposesHealthyGateway()
    {
        Skip.IfNot(_fixture.IsReady, _fixture.StartupSkipReason ?? "AspireHostFixture is not ready.");

        using var client = _fixture.CreateGatewayHttpClient();
        using var response = await client.GetAsync("/health");

        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected /health to be successful, got {(int)response.StatusCode}.");
    }
}
