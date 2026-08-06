# Skill: Blazor Routing — Route Gap Detection & Repair

**Category:** Blazor Frontend  
**Author:** Helly  
**Date:** 2026-05-29T07:50:34.836-04:00

---

## Problem Pattern

A URL (e.g. `/test-dashboard/`) reports a loading error or 404, but no build error is raised. The Blazor router silently falls through to `NotFound` when no component has a matching `@page` directive.

## Diagnosis Checklist

1. **Find all `.razor` files in `Components/Pages/`** — check for a file with the expected route.
2. **Check `Routes.razor`** — confirm `<Router AppAssembly>` is in place and `NotFoundPage` is wired.
3. **Check `App.razor`** — verify `<Routes />` is rendered inside `<body>`.
4. **Search for the `@page` directive** with the expected path:
   ```
   grep -r '@page "/test-dashboard"' src/OpenClawNet.Web
   ```
5. If the file is missing → create it. If it exists but has the wrong route string → fix the directive.

## Fix Template

Minimal Blazor Server page with loading state:

```razor
@page "/my-route"
@rendermode InteractiveServer

<PageTitle>My Page — OpenClawNet</PageTitle>

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" />
}
else
{
    <MudContainer MaxWidth="MaxWidth.Large" data-testid="my-page">
        <!-- content -->
    </MudContainer>
}

@code {
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        // load data
        _loading = false;
    }
}
```

## Nav Link Pattern

After creating the page, add a nav entry in `Components/Layout/NavMenu.razor`:

```html
<div class="nav-item px-3">
    <NavLink class="nav-link" href="my-route">
        <span class="bi bi-icon-name" aria-hidden="true"></span> Label
    </NavLink>
</div>
```

Use `Match="NavLinkMatch.All"` only for the home (`href=""`).

## Notes

- Blazor route matching is **case-insensitive** but the `@page` directive must exactly match the intended URL path.
- Trailing slashes: Blazor normalizes `/test-dashboard/` → `/test-dashboard` automatically.
- Sub-routes: for parametric routes, use `@page "/my-route/{Id}"` and add `[Parameter] public string Id { get; set; } = "";` in `@code`.
