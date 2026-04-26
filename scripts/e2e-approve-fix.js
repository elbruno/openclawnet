// E2E for approve-button fix verification.
// Uses Bruno's exact prompt and proves the markdown file is saved AFTER
// the user clicks Approve. Captures all browser console messages.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const WEB = process.env.OCN_WEB_URL || 'http://localhost:5215';
const PROMPT = 'Use the markdown_convert tool to fetch https://elbruno.com and save the result to a markdown file. Set save_to_file to true.';
const MD_DIR = 'C:\\Users\\brunocapuano\\OpenClawNet\\markdown_convert';

function ts() { return new Date().toISOString().slice(11, 23); }
const log = (m) => console.log(`[${ts()}] ${m}`);

function snapshotMd() {
  try {
    return new Map(fs.readdirSync(MD_DIR).map(n => {
      const st = fs.statSync(path.join(MD_DIR, n));
      return [n, st.mtimeMs];
    }));
  } catch { return new Map(); }
}

(async () => {
  const before = snapshotMd();
  log(`Baseline markdown_convert files: ${before.size}`);

  const browser = await chromium.launch({ headless: false, slowMo: 400 });
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();

  page.on('console', msg => {
    const t = msg.text();
    if (t.includes('APPROVE-CLICK') || t.includes('DENY-CLICK') || msg.type() === 'error') {
      log(`BROWSER[${msg.type()}]: ${t}`);
    }
  });
  page.on('pageerror', err => log(`PAGE EXCEPTION: ${err.message}`));

  try {
    log(`Navigate ${WEB}`);
    await page.goto(WEB, { waitUntil: 'networkidle' });

    log('Open Chat');
    await page.getByRole('link', { name: /chat/i }).first().click();
    await page.waitForLoadState('networkidle');

    log('+ New Chat');
    const newChatBtn = page.getByRole('button', { name: /\+\s*new chat/i }).first();
    if (await newChatBtn.isVisible().catch(() => false)) {
      await newChatBtn.click();
      await page.waitForTimeout(1500);
    }

    const input = page.locator('textarea').first();
    await input.waitFor({ state: 'visible', timeout: 15000 });
    log(`Send prompt: "${PROMPT}"`);
    await input.fill(PROMPT);
    await input.press('Enter');

    // Wait for any approve button up to 60s
    const approveBtn = page.locator('[data-testid="tool-approve-btn"]').first();
    const sawApproval = await approveBtn.waitFor({ state: 'visible', timeout: 60_000 })
      .then(() => true).catch(() => false);

    let approveClickedAt = 0;
    if (sawApproval) {
      log('✅ Approval card visible');
      await page.screenshot({ path: 'e2e-shot-approval.png' });
      approveClickedAt = Date.now();
      log('Click APPROVE');
      await approveBtn.click();
      log('  pointer click dispatched');

      const cleared = await approveBtn.waitFor({ state: 'detached', timeout: 30_000 })
        .then(() => true).catch(() => false);
      log(cleared ? '✅ Approve card cleared' : '❌ Approve card stuck');
      await page.screenshot({ path: 'e2e-shot-after-approve.png' });
    } else {
      log('No approval card — agent went direct');
    }

    // Now wait up to 180s for a NEW markdown file to land after approval
    log('Polling markdown_convert for NEW files (180s)...');
    const deadline = Date.now() + 180_000;
    let newFile = null;
    while (Date.now() < deadline) {
      const after = snapshotMd();
      for (const [name, mtime] of after) {
        if (!before.has(name) && (approveClickedAt === 0 || mtime >= approveClickedAt - 2000)) {
          newFile = { name, mtime, size: fs.statSync(path.join(MD_DIR, name)).size };
          break;
        }
      }
      if (newFile) break;
      await page.waitForTimeout(2000);
    }

    if (newFile) {
      log(`✅ NEW FILE: ${newFile.name} (${newFile.size} bytes, mtime=${new Date(newFile.mtime).toISOString()})`);
      const head = fs.readFileSync(path.join(MD_DIR, newFile.name), 'utf8').slice(0, 300);
      log(`First 300 chars:\n---\n${head}\n---`);
      log(`Approve→file delay: ${((newFile.mtime - approveClickedAt) / 1000).toFixed(1)}s`);
    } else {
      log('❌ No new markdown file appeared within 180s');
    }
    await page.screenshot({ path: 'e2e-shot-final.png', fullPage: true });
  } catch (e) {
    log(`FATAL: ${e.message}`);
  } finally {
    await page.waitForTimeout(2000);
    await browser.close();
  }
})();
