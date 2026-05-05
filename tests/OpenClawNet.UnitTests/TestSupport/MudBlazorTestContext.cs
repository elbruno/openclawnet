using Bunit;
using MudBlazor.Services;

namespace OpenClawNet.UnitTests.TestSupport;

/// <summary>
/// Base bUnit TestContext for tests that render MudBlazor components.
/// Adopts the official MudBlazor testing pattern:
///  - Registers MudBlazor services via AddMudServices()
///  - Sets JSInterop to Loose mode so MudBlazor's JS interop calls
///    (mudPopover, mudKeyInterceptor, mudScrollManager, etc.) silently
///    return defaults instead of throwing in tests.
/// 
/// Reference: https://mudblazor.com/docs/getting-started/unit-testing
/// See .squad/decisions.md (entry: "Adopt official MudBlazor + bUnit fixture pattern")
/// for the rationale and alternatives considered.
/// </summary>
public abstract class MudBlazorTestContext : TestContext
{
    protected MudBlazorTestContext()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
