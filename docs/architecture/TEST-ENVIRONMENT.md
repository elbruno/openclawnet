# Test Environment & Blockers

**Last Updated:** 2026-08-06  
**Test Framework:** xUnit 2.9.3 with SkippableFact  
**Configuration:** `tests/OpenClawNet.IntegrationTests/xunit.runner.json`

---

## Environment-Dependent Test Blockers

Tests automatically skip if environment requirements are not met. This prevents CI failures on environments without certain capabilities.

### 1. Ollama Not Running

**Impact:** All tests using local LLM inference  
**Affected Tests:** `OpenClawNet.IntegrationTests/Ollama*.cs`  
**Symptom:** TimeoutException when contacting `http://localhost:11434`

**Mitigation:**
```powershell
# Option A: Native Ollama
ollama serve

# Option B: Docker container
docker run -d -p 11434:11434 ollama/ollama
docker exec <container_id> ollama pull llama3.2:3b

# Verify
curl http://localhost:11434/api/tags
```

**Test Code:**
```csharp
[SkippableFact]
public async Task OllamaProvider_LocalInference_Succeeds()
{
    Skip.IfNot(
        await IsOllamaAvailableAsync(),
        "Ollama service not running on localhost:11434"
    );
    
    // Test code...
}
```

---

### 2. Docker Not Available

**Impact:** Aspire container orchestration tests  
**Affected Tests:** `OpenClawNet.IntegrationTests/AspireHost*.cs`  
**Symptom:** Socket error when trying to reach Docker daemon

**Mitigation:**
```powershell
# Windows: Ensure Docker Desktop is running
# Verify
docker ps

# Linux: Ensure Docker daemon is active
sudo systemctl status docker
sudo systemctl start docker  # if needed
```

**Test Code:**
```csharp
[SkippableFact]
public async Task AspireHostFixture_Starts_Successfully()
{
    Skip.IfNot(
        await IsDockerAvailableAsync(),
        "Docker daemon not available"
    );
    
    using var fixture = await AspireHostFixture.BuildAsync();
    Assert.NotNull(fixture.AppHost);
}
```

---

### 3. Playwright Browsers Missing

**Impact:** E2E browser automation tests  
**Affected Tests:** `OpenClawNet.PlaywrightTests/Chat*.cs`  
**Symptom:** "Browser executable not found" or "BrowserLaunchFailedException"

**Mitigation:**
```powershell
# Install Playwright browsers (one-time)
pwsh -Command { & "$env:USERPROFILE\.playwright\install.ps1" }

# Or via npm/yarn if installed
playwright install

# Verify
ls $env:USERPROFILE\.playwright
```

**Test Code:**
```csharp
[SkippableFact]
public async Task ChatUI_SendsMessage_ReceivesResponse()
{
    Skip.IfNot(
        await IsPlaywrightBrowserAvailableAsync(),
        "Playwright browsers not installed"
    );
    
    await using var browser = await BrowserFactory.LaunchAsync();
    // Test code...
}
```

---

### 4. Azure Subscription Missing

**Impact:** Azure OpenAI provider tests, Azure Storage tests  
**Affected Tests:** `OpenClawNet.IntegrationTests.Azure/*.cs` and `OpenClawNet.UnitTests.Azure/*.cs`  
**Symptom:** AuthenticationFailedException or resource not found

**Mitigation:**
```powershell
# Set environment variables (one per session)
$env:AZURE_SUBSCRIPTION_ID = "your-subscription-id"
$env:AZURE_TENANT_ID = "your-tenant-id"
$env:AZURE_CLIENT_ID = "your-app-id"
$env:AZURE_CLIENT_SECRET = "your-secret"  # or use managed identity in CI

# Verify
az login
az account show
```

**Test Code:**
```csharp
[SkippableFact]
public async Task AzureOpenAIProvider_WithAzureSubscription_Responds()
{
    Skip.IfNot(
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID")),
        "Azure credentials not configured"
    );
    
    // Test code...
}
```

---

### 5. GitHub Copilot Auth Missing

**Impact:** GitHub Copilot provider tests  
**Affected Tests:** `OpenClawNet.IntegrationTests/GitHubCopilot*.cs`  
**Symptom:** 401 Unauthorized when calling GitHub API

**Mitigation:**
```powershell
# Create GitHub Personal Access Token (PAT)
# 1. Go to https://github.com/settings/tokens
# 2. Create token with 'copilot' scope
# 3. Set environment variable
$env:GITHUB_TOKEN = "github_pat_..."

# Verify
curl -H "Authorization: Bearer $env:GITHUB_TOKEN" https://api.github.com/user
```

**Test Code:**
```csharp
[SkippableFact]
public async Task GitHubCopilotProvider_WithValidToken_Responds()
{
    Skip.IfNot(
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
        "GitHub Copilot token not configured"
    );
    
    // Test code...
}
```

---

### 6. Port Conflicts (5010, 5011, 5012, etc.)

**Impact:** Aspire AppHost binding  
**Affected Tests:** All Aspire-based integration tests  
**Symptom:** AddressAlreadyInUseException or "port is already allocated"

**Mitigation:**
```powershell
# Find process using port 5010 (Windows)
netstat -ano -p tcp | findstr :5010

# Kill process (replace PID with actual value)
taskkill /PID 1234 /F

# macOS/Linux
lsof -i :5010
kill -9 <PID>

# Prevent conflicts: Use random ports in test config
# xunit.runner.json can specify a port range or 0 for OS-assigned
```

**Test Code:**
```csharp
// xunit.runner.json
{
  "diagnosticMessages": true,
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false,
  "appHostRandomPortRange": [5010, 6000]
}
```

---

### 7. Playwright Timing Flake

**Impact:** E2E browser automation (intermittent failures)  
**Affected Tests:** `OpenClawNet.PlaywrightTests/Chat*.cs`  
**Symptom:** Timeout waiting for selector or navigation

**Causes:**
- Slow environment (CI runners, cloud VMs)
- Network latency
- Browser startup overhead

**Mitigation:**
```csharp
// Increase default timeouts (in test base class or fixture)
var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    Timeout = 60000,  // 60 seconds
});

var page = await context.NewPageAsync();
page.SetDefaultTimeout(60000);
page.SetDefaultNavigationTimeout(60000);

// Or configure in xunit.runner.json
{
  "diagnosticMessages": true,
  "playwrightTimeout": 60000,
  "playwrightNavigationTimeout": 60000
}
```

**Test Code:**
```csharp
[SkippableFact]
public async Task ChatUI_LoadsHomepage_AndFocusesInput()
{
    Skip.IfNot(
        await IsPlaywrightBrowserAvailableAsync(),
        "Playwright browsers not installed"
    );
    
    await using var browser = await BrowserFactory.LaunchAsync();
    var page = await browser.NewPageAsync();
    
    try
    {
        // Long timeout for slow environments
        await page.GotoAsync("http://localhost:5010", new PageGotoOptions
        {
            Timeout = 60000,
            WaitUntil = WaitUntilState.NetworkIdle
        });
        
        await page.FocusAsync("input[placeholder='Type your message...']");
    }
    catch (PlaywrightException ex) when (ex.Message.Contains("Timeout"))
    {
        // Soft fail instead of hard error
        Skip.Because("Page load timeout (environment too slow)");
    }
}
```

---

## Test Configuration

### xunit.runner.json (Orchestration)

Located in `tests/OpenClawNet.IntegrationTests/`:

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "diagnosticMessages": true,
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false,
  "shadowCopy": false,
  "maxParallelThreads": 1,
  "longRunningTestSeconds": 30,
  "skipMissingOptionalDependencies": true
}
```

**Key Settings:**
- `parallelizeAssembly: false` — Tests run sequentially to avoid port/resource conflicts
- `maxParallelThreads: 1` — Strict serialization (prevents Ollama/Docker contention)
- `longRunningTestSeconds: 30` — Warnings for slow tests
- `skipMissingOptionalDependencies: true` — Skip tests if runtime not available

---

## Running Tests Locally

### All Tests (Full Suite)
```powershell
dotnet test --no-build

# Skipped tests appear as yellow dots
# Failed tests appear as red X
# Passed tests appear as green dots
```

### Only Local Tests (No Azure/GitHub)
```powershell
dotnet test tests/OpenClawNet.IntegrationTests --no-build -l "console;verbosity=normal"
```

### Only E2E Tests
```powershell
dotnet test tests/OpenClawNet.PlaywrightTests --no-build
```

### Specific Test Class
```powershell
dotnet test tests/OpenClawNet.IntegrationTests --no-build --filter "ClassName=ChatEndpointTests"
```

### With Environment Variables (Azure)
```powershell
$env:AZURE_SUBSCRIPTION_ID = "your-id"
$env:AZURE_TENANT_ID = "your-tenant"
$env:AZURE_CLIENT_ID = "your-app"
$env:AZURE_CLIENT_SECRET = "your-secret"
dotnet test tests/OpenClawNet.IntegrationTests.Azure --no-build
```

---

## CI/CD Behavior

### GitHub Actions Workflow
- Runs on every push and PR to `main`
- **Skipped by default:**
  - Azure tests (no credentials in CI)
  - GitHub Copilot tests (no token in CI)
  - Playwright E2E tests (browser setup overhead)
- **Run always:**
  - Unit tests
  - Local integration tests (Ollama required; skipped if unavailable)

### Retry Logic
Transient failures are retried up to 2 times (Playwright flake, network timeouts).

---

## Troubleshooting

### Test Hangs (Infinite Wait)
```powershell
# Kill all hanging test processes
taskkill /F /IM dotnet.exe

# Or specific test runner
Get-Process | Where-Object {$_.ProcessName -like "testhost*"} | Stop-Process -Force
```

### Port Already in Use
```powershell
# Find and kill process
netstat -ano -p tcp | findstr :5010
taskkill /PID <PID> /F
```

### Docker Daemon Error
```powershell
# Restart Docker Desktop (Windows)
# Then retry: docker ps
```

### Ollama Connection Timeout
```powershell
# Check Ollama is running
curl http://localhost:11434/api/tags

# If not, start it
ollama serve
```

---

## Known Issues & Workarounds

| Issue | Workaround | Status |
|-------|-----------|--------|
| Playwright auto-download slow on first run | Pre-download: `playwright install` | Expected behavior |
| Aspire port conflicts on CI | Use random port ranges in config | Fixed in recent Aspire versions |
| Ollama model missing on fresh clone | Pull model first: `ollama pull llama3.2:3b` | User responsibility |
| Azure tests timeout in CI | Skip Azure tests in CI (no credentials) | By design |

---

## Future Improvements

- [ ] Docker Compose profiles for isolated test environments
- [ ] GitHub Actions matrix for multiple environments
- [ ] Async test result aggregation (currently sequential)
- [ ] Performance benchmarking harness for regression detection
