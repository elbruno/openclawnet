# Skill: Migrate a Bootstrap table to MudBlazor MudDataGrid

@extracted: 2026-04-22, helly, from Tool Log table migration (9 pages validated)  
@validated-by: helly (high), petey (high), irving (medium)

**Owner:** Helly (Frontend Dev)
**Repo:** elbruno/openclawnet-plan (Blazor Server, .NET 10, MudBlazor 9.3.0)
**Confidence:** High — validated across 9 pages including ServerData (Tool Log + Tools + MCP Settings + Job Templates + Agent Profiles + Model Providers + Sessions + Job Run Events + Jobs + Job Detail). The class of latent circuit crashes documented under "Critical prerequisite" is fixed and verified in production.
**Created:** 2026-04-22

## When to use

You're migrating a hand-rolled `<table class="table">` (or a card grid) on an OpenClawNet.Web `.razor` page to `<MudDataGrid<T>>`, and you want sort/filter/paging/density/column-visibility/sticky-header for free without redoing theme work.

Prerequisites already in place on this repo (do NOT redo):
- MudBlazor 9.3.0 referenced from `OpenClawNet.Web.csproj`.
- `AddMudServices()` in `Program.cs`.
- `MudThemeProvider Theme="AppTheme.Default"` + `MudPopoverProvider`/`MudDialogProvider`/`MudSnackbarProvider` in `Components/Layout/MainLayout.razor`, **each tagged with `@rendermode="InteractiveServer"`** (see Critical prerequisite below).
- `@using MudBlazor` and `@using OpenClawNet.Web.Theme` in `Components/_Imports.razor`.
- MudBlazor CSS/JS linked in `Components/App.razor` after Bootstrap.

If those aren't in place, see commit `e7fe21a` (foundation) before migrating tables.

## Recipe

1. **Identify what's data and what's chrome.** Find the data source (e.g. `_servers`, `_templates`, `_tools`) and any per-row lookup dictionaries (e.g. `_results`, `_lastTest`). These all stay. Only the rendering layer changes. Do NOT touch event handlers, `LoadAsync`, or DTOs.

2. **Add density state.** In `@code`:
   ```csharp
   private bool _dense = true;
   ```

3. **Replace the `<table>` block.** Skeleton:
   ```razor
   <MudDataGrid T="MyDto"
                Items="_items"
                Dense="_dense"
                Hover="true"
                Striped="true"
                Bordered="false"
                Filterable="true"
                SortMode="SortMode.Single"
                ShowColumnOptions="true"
                Groupable="false"
                FixedHeader="true"
                Height="70vh"
                Elevation="0">
       <ToolBarContent>
           <MudText Typo="Typo.subtitle2">@_items.Count item(s)</MudText>
           <MudSpacer />
           <MudSwitch T="bool" Value="_dense" ValueChanged="v => _dense = v"
                      Color="Color.Primary" Label="@(_dense ? "Compact" : "Comfortable")" />
       </ToolBarContent>
       <Columns>
           <!-- one entry per old <th> -->
       </Columns>
       <ChildRowContent>
           <!-- old colspan sub-row content goes here -->
       </ChildRowContent>
       <PagerContent>
           <MudDataGridPager T="MyDto" PageSizeOptions="new[] { 10, 25, 50, 100 }" />
       </PagerContent>
   </MudDataGrid>
   ```

4. **Map columns:**
   - Real property + plain text → `<PropertyColumn Property="x => x.Foo" Title="..." />`.
   - Real property + custom rendering (badges, code, links) → `<PropertyColumn Property="x => x.Foo" Title="..."><CellTemplate>...</CellTemplate></PropertyColumn>`. `context.Item` gives you the DTO.
   - Derived value or actions → `<TemplateColumn Title="..." Sortable="false" Filterable="false"><CellTemplate>...</CellTemplate></TemplateColumn>`.
   - Derived value where you DO want to sort → `<TemplateColumn ... SortBy="x => /* int or string projection */">`.
   - Right-aligned (e.g. action buttons) → `CellClass="text-end"` on the column.

5. **Sub-rows go in `ChildRowContent`.** See decision `helly-mudblazor-pr2.md`. Wrap content in `<MudCard Elevation="0" Class="pa-2 mud-background-gray">` to get the muted background that matches Bootstrap's striped sub-row look. The grid auto-adds the caret column — do not declare it yourself.

6. **Bootstrap → Mud equivalents:**
   - `<span class="badge bg-secondary">` → `<MudChip T="string" Size="Size.Small" Color="Color.Secondary" Variant="Variant.Filled">`
   - `<span class="badge bg-light text-dark border">` → `<MudChip T="string" Size="Size.Small" Color="Color.Default" Variant="Variant.Outlined">`
   - `<span class="badge bg-success">` → `Color.Success`, `bg-warning` → `Color.Warning`, `bg-danger` → `Color.Error`, `bg-info` → `Color.Info`.
   - Bootstrap buttons (`btn btn-sm btn-outline-primary`) — keep them as-is when their `@onclick` is calling existing handlers. They render fine inside `CellTemplate` and avoid needless visual churn vs. mixing `MudButton` in.

7. **Always specify `T=`** on `MudChip`, `MudSwitch`, `MudDataGridPager`. They are generic in v9; omitting `T=` produces CS0411 inference errors.

8. **`MudSwitch`:** prefer `Value="..." ValueChanged="v => ..."` over `@bind-Value` when state is also touched in `@code`.

9. **`data-testid` for UI tests:** add it to an inner element inside `CellTemplate` (e.g. on a `<code>` tag with the DTO's name). MudDataGrid does not expose a per-row attribute hook, but tests don't care — they target a stable inner element.

10. **Sort the data BEFORE assigning to `Items`** if you want a default order (e.g. `Items="_tools.OrderBy(x => x.Category).ThenBy(x => x.Name)"`). Or set `InitialDirection="SortDirection.Descending"` on a `PropertyColumn` (works for the default sort column).

## Build & verify

```powershell
$env:NUGET_PACKAGES="$env:USERPROFILE\.nuget\packages2"
dotnet build src\OpenClawNet.Web\OpenClawNet.Web.csproj --verbosity quiet
dotnet test  tests\OpenClawNet.UnitTests\OpenClawNet.UnitTests.csproj --filter "Category!=Live" --verbosity normal --no-build
```

Baseline: 525 passed / 1 skipped / 1 pre-existing failure (`DocumentPipelineTests.FileSystemTool_ListDirectory_ReturnsSampleDocs` — depends on the four Northwind PDFs that may be missing from the working tree). Anything beyond that is your fault.

If the build complains about a locked `OpenClawNet.Web.dll`, Aspire is still running. Find and stop it:
```powershell
Get-Process | Where-Object Name -match 'OpenClawNet'
Stop-Process -Id <pid>
```

## Commit conventions

- One commit per page.
- Subject: `feat(web): migrate <Page Name> table to MudDataGrid`.
- Body: which features (sort/filter/paging/density/column-visibility), what moved into `ChildRowContent` (if anything), what was preserved (data source, handlers, test ids).
- Trailer (mandatory):
  ```
  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
  ```

## Critical prerequisite — providers must be InteractiveServer

In a Blazor Web App with **per-page `@rendermode InteractiveServer`** (which is what every page in this repo uses), the `MainLayout` is part of the static-rendered render tree. Any MudBlazor provider declared in `MainLayout` without an explicit rendermode therefore renders **statically** — its interactive backing service (e.g. `PopoverService.IsInitialized`) is never set on the circuit.

Symptom: any page that renders a `MudDataGrid` with rows throws an unhandled circuit exception on first interactive render:

```
System.InvalidOperationException: Missing <MudPopoverProvider />, please add it to your layout.
   at MudBlazor.PopoverService.CreatePopoverAsync(...)
   at MudBlazor.MudPopoverBase.OnInitializedAsync()
```

The exception is silent if the page renders the grid only when `_items.Count > 0` and the page happens to be empty during testing — that's how Tool Log, MCP Settings, and Job Templates passed verification while the bug was already present. The Tools page surfaced it because it always has data on a fresh install.

**Fix (already applied in `MainLayout.razor`):**

```razor
<MudThemeProvider Theme="OpenClawNet.Web.Theme.AppTheme.Default" @rendermode="InteractiveServer" />
<MudPopoverProvider @rendermode="InteractiveServer" />
<MudDialogProvider @rendermode="InteractiveServer" />
<MudSnackbarProvider @rendermode="InteractiveServer" />
```

**Verification step (do this for every migrated page before declaring it done):** start Aspire, navigate to the page **with seeded/non-empty data**, watch `aspire otel logs web --severity Error`. Empty-state-only smoke testing is not sufficient.

## What can go wrong

- **Card grid → DataGrid:** legitimate when the cards are heterogeneous-but-sortable (e.g. Job Templates). Just collapse the long-form fields into `ChildRowContent`. Don't try this for genuinely visual layouts (Skills page is correctly a card grid because each card has a logo/screenshot).
- **`<details>` inside cells:** don't. Use `ChildRowContent` so the grid manages expansion state and you don't end up with two competing expand mechanisms per row.
- **Wide rows fighting the page width:** drop the column count (push fields into `ChildRowContent`), turn on column visibility (`ShowColumnOptions="true"` is already in the skeleton), or set `Dense="true"` by default.
- **Per-row dictionaries (e.g. `_lastTest[s.Id]`):** access them inside `CellTemplate` via `context.Item.Id`. They work fine; they don't need to be projected onto the DTO.

## Decisions referenced

- `.squad/decisions/inbox/helly-mudblazor-pr2.md` — `ChildRowContent` vs `RowTemplate` ruling.
- `.squad/decisions/inbox/helly-mudblazor-rest-of-tables.md` — final rollout (Agent Profiles, Model Providers, Sessions, Job Run Events, Jobs, Job Detail) + ServerData pattern.
- Foundation commit `e7fe21a` — MudBlazor + AppTheme setup.
- Pilot commit `fa1628c` — Tool Log reference implementation.

## ServerData (server-side paging/sort/filter)

For pages that may grow unbounded (Sessions, Job Run Events), use the `ServerData`
callback instead of `Items=`:

```razor
<MudDataGrid @ref="_grid" T="MyDto"
             ServerData="LoadGridDataAsync"
             ...>
```

```csharp
private MudDataGrid<MyDto>? _grid;

private Task<GridData<MyDto>> LoadGridDataAsync(GridState<MyDto> state, CancellationToken ct)
{
    IEnumerable<MyDto> q = _source;
    foreach (var fd in state.FilterDefinitions) q = q.Where(fd.GenerateFilterFunction());
    foreach (var sd in state.SortDefinitions)
        q = sd.Descending ? q.OrderByDescending(x => sd.SortFunc(x))
                          : q.OrderBy(x => sd.SortFunc(x));
    var list = q.ToList();
    var page = list.Skip(state.Page * state.PageSize).Take(state.PageSize).ToList();
    return Task.FromResult(new GridData<MyDto> { Items = page, TotalItems = list.Count });
}
```

**The `CancellationToken` parameter is mandatory in MudBlazor v9.** Without it the
C# compiles, but the Razor source generator emits
`error CS0123: No overload for 'LoadGridDataAsync' matches delegate
'Func<GridState<T>, CancellationToken, Task<GridData<T>>>'` at the
`ServerData=` attribute. Always declare it (you can ignore it).

When the data source is in-memory (because the upstream API doesn't yet support
paging), this is still a win: only the visible page renders. Once the API gains
`?skip=&take=`, swap the body of `LoadGridDataAsync` for an HTTP call — no Razor
or callsite changes.

When you have **external filter chrome** (search box, date dropdown) that lives
outside the grid, call `await _grid.ReloadServerData()` from the change handler
after mutating your local filter state. Without that the grid keeps showing the
stale page.

## Selection toolbars

If the page already has a `HashSet<TKey> _selected` plus a bulk-action toolbar,
keep it as-is and render selection as a `TemplateColumn`:

```razor
<TemplateColumn Title="" Sortable="false" Filterable="false">
    <HeaderTemplate>
        <input class="form-check-input" type="checkbox"
               checked="@AllSelected" @onchange="ToggleSelectAll" />
    </HeaderTemplate>
    <CellTemplate>
        <input class="form-check-input" type="checkbox"
               checked="@_selected.Contains(context.Item.Id)"
               @onchange="e => ToggleItem(context.Item.Id, e)" />
    </CellTemplate>
</TemplateColumn>
```

This is cheaper than switching to MudDataGrid's built-in `MultiSelection`, which
would force you to rewrite all bulk-action handlers and any disable-conditions
that depend on `_selected.Count`.
