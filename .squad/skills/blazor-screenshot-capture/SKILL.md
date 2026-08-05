# Skill: Aspire + Playwright Blazor Server screenshot capture

@extracted: 2026-04-27, petey, from documentation screenshot workflows  
@validated-by: petey (high), helly (medium)

## When to use

You need to capture screenshots of Blazor Server pages in `OpenClawNet.Web` for documentation, design review, regression baselines, or PR demos. The app runs under Aspire orchestration (gateway + scheduler + several backend services + Web). Pages call the gateway over the Aspire-injected service-discovery URLs.

## Why "just hit the URL with Playwright" doesn't work out of the box

1. **Random ports.** Aspire assigns dynamic ports to `web`, `gateway`, etc. on every run. The well-known `5010` is the gateway's reverse-proxy/router endpoint — its API works (`/api/tools`) but `/` returns 404. The Web app gets a different random port each session.
2. **Blazor Server prerender + SignalR re-render.** Pages that load data in `OnInitializedAsync` render twice: once during server prerender (with `_loading=true` placeholders), then again on the interactive circuit after SignalR connects. Playwright's `waitUntil: 'networkidle'` fires *before* SignalR settles because SignalR keeps a long-lived connection. Result: you screenshot the "Loading…" placeholder instead of the data.
3. **Playwright MCP wants Chrome.** The default `@playwright/mcp` config uses `--browser chrome`. If the machine doesn't have Google Chrome and the user can't run `playwright install chrome` as admin, the MCP fails. The fallback is **chromium** (which `playwright install chromium` provides without admin).

## Recipe

### 1. Start Aspire and discover the Web port

```powershell
aspire start src\OpenClawNet.AppHost   # detaches; no interactive menu in current version
```

Wait for `✔ Apphost started successfully`. Then find the Web app's port:

```powershell
$webPid = (Get-Process OpenClawNet.Web).Id
$webPort = (Get-NetTCPConnection -State Listen -OwningProcess $webPid | Select-Object -First 1).LocalPort
"http://localhost:$webPort"
```

Sanity-check with `Invoke-WebRequest "http://localhost:$webPort/"` — the page title should be `OpenClaw .NET - Chat` (the home route).

### 2. Warm the gateway

The gateway often takes 20–30 s to respond to its first call (cold compile). Pre-warm any endpoints the page you're about to capture will hit:

```powershell
Invoke-WebRequest http://localhost:5010/api/tools | Out-Null
Invoke-WebRequest http://localhost:5010/api/agent-profiles?kind=Standard | Out-Null
```

### 3. Capture with Playwright (Node, bypassing the MCP)

Install the npm package locally if needed (`npm install playwright`), then chromium (`npx playwright install chromium` — no admin required).

```js
const { chromium } = require('playwright');

const BASE = 'http://localhost:58604';   // ← from step 1
const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  ignoreHTTPSErrors: true,
});
const page = await ctx.newPage();
page.setDefaultTimeout(30000);

// Surface circuit errors so you don't ship a "Loading…" placeholder for a broken page
page.on('pageerror', e => console.error('[pageerror]', e.message));
page.on('console', m => { if (m.type() === 'error') console.error('[console.error]', m.text()); });

await page.goto(`${BASE}/tools`, { waitUntil: 'domcontentloaded' });

// IMPORTANT: give SignalR time to connect and re-render
await page.waitForTimeout(15000);

// Belt-and-braces: poll until the loading placeholder is gone
try {
  await page.waitForFunction(
    (txt) => !document.body.innerText.includes(txt),
    'Loading tools',
    { timeout: 30000 }
  );
} catch {
  console.warn('Page still in loading state — likely a real circuit error, not a timing issue');
}

await page.waitForTimeout(2000);
await page.screenshot({ path: 'C:\\path\\to\\image.png', fullPage: false });
await browser.close();
```

### 4. Toggling form variants (Model Providers Add Provider, etc.)

Pages with conditional sub-forms keyed off a `<select @bind="_form.X">` need the **backend** value, not the display label:

```js
await page.locator('button:has-text("Add Provider")').first().click();
await page.waitForTimeout(800);
await page.locator('select').first().selectOption({ value: 'azure-openai' });
//                                                              ^^^^^^^^^^^^
// kebab-case backend key — NOT 'Azure OpenAI' (the visible label)
```

Backend keys for `ModelProviders`: `ollama`, `azure-openai`, `github-copilot`, `foundry`, `foundry-local`, `lm-studio`. (Source: `src/OpenClawNet.Web/Components/Pages/ModelProviders.razor` `_form.ProviderType` checks.)

### 5. Stop Aspire when done

```powershell
aspire stop
# residual children sometimes linger:
Get-Process | Where-Object { $_.ProcessName -match 'OpenClawNet' } |
  ForEach-Object { Stop-Process -Id $_.Id -Force }
```

## Honesty rules

- **Empty states are real states.** If `/tool-log` shows "No tool executions recorded yet" because nothing has happened, screenshot that — don't fabricate fake rows.
- **Broken pages are NOT real states.** If the page throws an unhandled circuit exception (you'll see a `[pageerror]` from the Playwright listener), don't ship a "Loading…" placeholder. Defer the screenshot and file a bug.
- **Pre-populate only with real interactions.** If you want a non-empty Chat / Sessions / Tool Log capture, drive a real chat round-trip in the same Playwright run. Don't hand-craft DTOs and POST them past the UI.

## Image conventions in `docs/manuals/`

`docs/manuals/images/<chapter-slug>/<NN-name>.png` — `<chapter-slug>` matches the manual filename without `.md`, `NN` matches the order of references inside the manual. Reuse existing names so you don't have to edit the markdown — overwriting in place keeps the diff to PNG bytes only.

## Anti-patterns

- ❌ Hardcoding Web port `5215` from `launchSettings.json` — Aspire ignores that and assigns its own.
- ❌ Using `waitUntil: 'networkidle'` and screenshotting immediately on Blazor Server pages.
- ❌ Configuring the playwright MCP to use Chrome on a machine without Chrome — switch to `--browser chromium` in `.mcp.json` *or* bypass the MCP and call the npm package from Node.
- ❌ Faking `/tool-log` rows by writing directly to the DB or stubbing the gateway response.
