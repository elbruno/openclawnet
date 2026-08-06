---
name: mudblazor-blazor-server-setup
description: "Wire MudBlazor v9 into a .NET 10 Blazor Server app while keeping Bootstrap as the layout framework — without pulling in Material fonts."
category: frontend
tags:
  - blazor
  - mudblazor
  - dotnet-10
  - bootstrap
  - theming
examples:
  - "Add MudBlazor MudDataGrid to a Bootstrap-based Blazor Server app"
  - "Map a Bootstrap palette into a MudBlazor MudTheme"
  - "Use MudBlazor components without switching the app's typography to Roboto"
enabled: true
---

# MudBlazor on Blazor Server (.NET 10) without losing Bootstrap

@extracted: 2026-04-27, helly, from MudBlazor foundation commit (e7fe21a)  
@validated-by: helly (high), petey (high)

Use this skill when adopting MudBlazor in a Blazor Server app that already
ships Bootstrap and you want to:
- keep Bootstrap for layout + non-data-table pages,
- use MudBlazor for data tables (`MudDataGrid`) and other rich components,
- keep the existing Bootstrap palette + typography (no Roboto/Material fonts).

## 5-step setup

### 1. Install (one command)
```powershell
dotnet add src/<WebProject>/<WebProject>.csproj package MudBlazor
```
On .NET 10 the latest stable (9.x) resolves cleanly.

### 2. Register services
In `Program.cs`:
```csharp
using MudBlazor.Services;
// ...
builder.Services.AddMudServices();
```

### 3. Add CSS + JS to `App.razor`
Order matters: load MudBlazor **after** Bootstrap so any future overrides win.
```razor
<link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />
<link rel="stylesheet" href="@Assets["app.css"]" />
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
...
<script src="@Assets["_framework/blazor.web.js"]"></script>
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

### 4. Add providers to the **root layout**, BEFORE the page chrome
In `Components/Layout/MainLayout.razor`:
```razor
@inherits LayoutComponentBase

<MudThemeProvider Theme="MyApp.Theme.AppTheme.Default" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<div class="page">
    ...existing Bootstrap layout...
</div>
```
Putting providers inside the layout's `<main>` works but causes z-index issues with popovers/dialogs.

### 5. Custom theme — Bootstrap palette + your existing font stack
Create `Theme/AppTheme.cs`:
```csharp
public static class AppTheme
{
    private static readonly string[] FontStack =
        { "'Helvetica Neue'", "Helvetica", "Arial", "sans-serif" };

    public static readonly MudTheme Default = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1b6ec2", PrimaryDarken = "#1861ac",
            Secondary = "#6c757d", Info = "#0dcaf0",
            Success = "#198754", Warning = "#ffc107", Error = "#dc3545",
            // Background/Surface = "#fff", LinesDefault = "#dee2e6", etc.
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontStack, FontSize = "0.9rem" },
            H1 = new H1Typography { FontFamily = FontStack },
            // ...repeat for H2-H6, Body1/2, Button, Caption, Subtitle1/2, Overline
        },
    };
}
```

**Critical:** override the FontFamily on **every typography level** (H1–H6, Body1/2, Button, Caption, Subtitle1/2, Overline). Setting only `Default` still leaves H1–H6 using MudBlazor's Roboto default.

`FontFamily` is `string[]`, not `string`. Reuse one constant.

In `_Imports.razor`:
```razor
@using MudBlazor
@using MyApp.Theme
```

## API gotchas (MudBlazor v9, .NET 10)

- `MudChip` and `MudSwitch` are now generic — must specify `T="string"`, `T="bool"`, etc.
- `MudSwitch` two-way binding with `@bind-Value` plus external state can fight you; use explicit `Value="..." ValueChanged="v => ..."`.
- `<MudDataGrid<T>>`:
  - `<PropertyColumn Property="x => x.Foo" Title="..." />` — lambda, not string.
  - Badges/chips → `<CellTemplate>`.
  - Expandable detail row → `<ChildRowContent>` (auto-adds caret column).
  - Pager → `<MudDataGridPager T="MyRow" PageSizeOptions="new[] { 10, 25, 50, 100 }" />` inside `<PagerContent>`.

## Build gotcha (Windows + Aspire)

If Aspire AppHost is running, `dotnet build` of the Web project fails with `MSB3027 — DLL locked by <project>.exe`. Stop the AppHost (cascades to children) with `Stop-Process -Id <pid>` after `Get-Process | Where { $_.ProcessName -match 'OpenClawNet' }`. Restart Aspire after building.
