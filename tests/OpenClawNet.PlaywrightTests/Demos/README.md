# 🎬 Demo-Only E2E Tests (Attached Aspire)

⚠️ **NOT FOR CI OR REGRESSION TESTING** ⚠️

This folder contains E2E tests that **ATTACH to an already-running Aspire instance** (`aspire start`) instead of booting Aspire in-process. These are designed for **live demos and voice-over recording** where the Aspire dashboard must stay visible to the audience.

---

## 📋 What's in This Folder

- **`AttachedAspireTestBase.cs`** — Base class for demo tests. Launches Playwright ALWAYS headed with SlowMo, connects to a running Aspire instance via env vars.
- **`PirateJourneyAttachedTests.cs`** — Demo-friendly twin of `SkillsPirateJourneyE2ETests.cs`. Same user journey, different infra.

---

## ✅ When to Use These Tests

- ✅ **Live conference demos** — Aspire dashboard visible behind the browser during the talk
- ✅ **Voice-over recording** — Combined with `PLAYWRIGHT_SLOWMO`, gives you a smooth presenter loop
- ✅ **Fast iteration during rehearsal** — Test attaches in 2–3s vs 30–60s cold start

---

## ❌ When NOT to Use These Tests

- ❌ **CI/CD pipelines** — Use the parent folder's `*JourneyE2ETests.cs` instead (in-process Aspire via `AppHostFixture`)
- ❌ **Regression testing** — Use the in-process tests instead
- ❌ **Automated validation** — Use the in-process tests instead

**For standard CI/regression coverage**, see:
- `tests\OpenClawNet.PlaywrightTests\SkillsPirateJourneyE2ETests.cs`
- `tests\OpenClawNet.PlaywrightTests\AppHostFixture.cs`

---

## 🚀 How to Run

### Step 1: Start Aspire (Terminal 1)

```powershell
aspire start src\OpenClawNet.AppHost
```

Wait for:
- Green health checks for all resources (web, gateway, scheduler, etc.)
- Aspire dashboard opens in your browser (default: `http://localhost:15178`)

### Step 2: Run the Test (Terminal 2)

```powershell
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
$env:PLAYWRIGHT_HEADED = "true"
$env:PLAYWRIGHT_SLOWMO = "1500"  # 800=fast, 1500=default, 2500=slow

# Optional: override URLs if your ports differ
# Use `aspire show-links` to discover the actual runtime URLs
# $env:OPENCLAW_WEB_URL = "https://localhost:7294"
# $env:OPENCLAW_GATEWAY_URL = "https://localhost:7067"

dotnet test tests\OpenClawNet.PlaywrightTests `
  --filter "Category=DemoLive&FullyQualifiedName~PirateJourneyAttachedTests"
```

### Step 3: Watch the Browser + Dashboard

The browser window will open headed, with a visible slow-mo delay between steps. The Aspire dashboard stays visible in the background throughout the test run.

---

## 🔧 Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `OPENCLAW_WEB_URL` | `https://localhost:7294` | Blazor frontend URL (use `aspire show-links` to find actual URL) |
| `OPENCLAW_GATEWAY_URL` | `https://localhost:7067` | Gateway API URL (use `aspire show-links` to find actual URL) |
| `PLAYWRIGHT_HEADED` | Always `true` for demos | Launches Chromium visible |
| `PLAYWRIGHT_SLOWMO` | `1500` ms | Inter-step delay for voice-over pacing (800=fast, 2500=slow) |

---

## 🏷️ Trait Convention

All tests in this folder are marked:

```csharp
[Trait("Category", "DemoLive")]
```

This excludes them from default CI runs:

```powershell
# CI (skips demo tests)
dotnet test --filter "Category!=Live"

# Demo (runs only demo tests)
dotnet test --filter "Category=DemoLive"
```

---

## 📖 Speaker Script Reference

For the full presenter commands and voice-over beats, see:

**`docs\sessions\session-3\speaker-script.md`** — Demo 1b (Pirate Skill — Aspire already running)
