# Skill: Scaffold Aspire-Registered Blazor Server Project

@extracted: 2026-04-23, mark, from Job Output Dashboard Phase 1 implementation  
@validated-by: petey (high), helly (high), irving (medium)

**Author:** Mark (Lead Architect)  
**Date:** 2026-04-23  
**Context:** Job Output Dashboard Phase 1 — OpenClawNet.Channels website

---

## Purpose

Scaffold a new Blazor Server project registered in Aspire with service discovery, health checks, and MudBlazor UI components.

---

## Pattern

### 1. Create Project

```powershell
cd src
dotnet new blazor -n <ProjectName> -o <ProjectName> --framework net10.0 --interactivity Server --all-interactive false --empty
```

### 2. Update .csproj

Add ServiceDefaults reference + packages:

```xml
<ItemGroup>
  <ProjectReference Include="..\OpenClawNet.ServiceDefaults\OpenClawNet.ServiceDefaults.csproj" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="MudBlazor" Version="9.3.0" />
  <!-- Add other packages as needed (e.g., Markdig for markdown) -->
</ItemGroup>
```

### 3. Update Program.cs

Wire up Aspire defaults + MudBlazor + named HttpClients:

```csharp
using MudBlazor.Services;
using <ProjectName>.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Named HttpClient for Gateway (or other service)
builder.Services.AddHttpClient("gateway", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var gatewayUrl = config["Services:gateway:https:0"]
        ?? config["Services:gateway:http:0"]
        ?? config["Gateway:BaseUrl"]
        ?? "https://localhost:7100";
    client.BaseAddress = new Uri(gatewayUrl.TrimEnd('/') + "/");
});

var app = builder.Build();

// ... standard middleware ...

app.MapDefaultEndpoints();  // ⚠️ Required for health checks
app.Run();
```

### 4. Update MainLayout.razor

MudBlazor providers must have `@rendermode="InteractiveServer"`:

```razor
@inherits LayoutComponentBase

<MudThemeProvider Theme="@_theme" @rendermode="InteractiveServer" />
<MudPopoverProvider @rendermode="InteractiveServer" />
<MudDialogProvider @rendermode="InteractiveServer" />
<MudSnackbarProvider @rendermode="InteractiveServer" />

<MudLayout>
    <!-- Your layout here -->
</MudLayout>

@code {
    private MudTheme _theme = new();
}
```

### 5. Update _Imports.razor

Add MudBlazor namespace:

```razor
@using MudBlazor
```

### 6. Add to Solution

```powershell
dotnet sln <SolutionFile>.slnx add src/<ProjectName>/<ProjectName>.csproj
```

### 7. Register in AppHost

Edit `src/OpenClawNet.AppHost/OpenClawNet.AppHost.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\<ProjectName>\<ProjectName>.csproj" />
</ItemGroup>
```

Edit `src/OpenClawNet.AppHost/AppHost.cs`:

```csharp
var myProject = builder.AddProject<Projects.<ProjectName_Underscores>>("my-resource-name")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(gateway)  // If needs Gateway
    .WaitFor(gateway);
```

**Service discovery URL injection:**

```csharp
// To allow another service to call this one:
otherService.WithEnvironment("Services__my-resource-name__https__0", myProject.GetEndpoint("https"));
```

### 8. Update launchSettings.json

Set dev ports:

```json
{
  "profiles": {
    "http": {
      "applicationUrl": "http://localhost:5XXX"
    },
    "https": {
      "applicationUrl": "https://localhost:7XXX;http://localhost:5XXX"
    }
  }
}
```

### 9. Build & Verify

```powershell
$env:NUGET_PACKAGES="$env:USERPROFILE\.nuget\packages2"
dotnet build src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj --verbosity quiet
```

---

## Common Issues

### MudBlazor Providers Not Rendered

**Symptom:** `InvalidOperationException: Missing <MudPopoverProvider />`

**Fix:** Add `@rendermode="InteractiveServer"` to each MudBlazor provider in MainLayout.

### Projects.* Type Not Found in AppHost

**Symptom:** `error CS0246: The type or namespace name 'Projects.MyProject' could not be found`

**Fix:** Add `<ProjectReference>` in `OpenClawNet.AppHost.csproj` for the new project. The Aspire SDK generates the `Projects.*` types from project references.

### MudChip Generic Type Error

**Symptom:** `error RZ10001: The type of component 'MudChip' cannot be inferred`

**Fix:** Specify generic type parameter: `<MudChip T="string">`

---

## References

- Decision: `.squad/decisions.md` § Job Output Dashboard (2026-04-23)
- Implementation: `src/OpenClawNet.Channels/` (Helly + Irving, commits 6ffeca3 + f7bc624)
- Aspire Docs: https://learn.microsoft.com/en-us/dotnet/aspire/
- MudBlazor Docs: https://mudblazor.com/
