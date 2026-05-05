# Test Support

Shared test infrastructure for OpenClawNet unit tests.

## MudBlazorTestContext

Base bUnit `TestContext` for tests that render any Blazor component using MudBlazor.

**Why:** MudBlazor components (MudTextField, MudMenu, MudDialog, etc.) require:
1. MudBlazor DI services (registered via `Services.AddMudServices()`)
2. Provider components (MudPopoverProvider, MudDialogProvider, MudSnackbarProvider) which normally live in `MainLayout.razor` but aren't present in bUnit's default render tree
3. Many JavaScript interop calls (mudPopover.connect, mudKeyInterceptor.connect, mudScrollManager, mudElementRef.saveFocus, etc.) that have no JS runtime in tests

`MudBlazorTestContext` solves all three by:
- Calling `AddMudServices()` in the constructor
- Setting `JSInterop.Mode = JSRuntimeMode.Loose` so unhandled JS calls return defaults silently rather than throwing

**Usage:**

```csharp
public class MyComponentTests : MudBlazorTestContext
{
    [Fact]
    public void MyComponent_Renders()
    {
        var cut = RenderComponent<MyComponent>();
        cut.Find("button").Click();
        // assertions...
    }
}
```

If a test renders a component that requires `<MudPopoverProvider/>` directly (e.g. `MudPopover` itself, not just a `MudTextField` that uses one), wrap the render:

```csharp
var cut = RenderComponent<MudPopoverProvider>(p => p.AddChildContent<MyComponent>());
```

**Reference:** https://mudblazor.com/docs/getting-started/unit-testing
**Decision rationale:** See `.squad/decisions.md` — "Adopt official MudBlazor + bUnit fixture pattern".
