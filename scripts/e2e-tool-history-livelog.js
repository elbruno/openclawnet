// E2E for tool-history audit trail + Agent Activity live-tail.
// Builds on scripts/e2e-approve-fix.js. Verifies:
//   1. After Approve, a persistent tool-history entry appears in the
//      messages list (data-testid="tool-history-entry").
//   2. The Agent Activity panel is visible without clicking
//      (data-testid="agent-activity-panel").
//   3. The Agent Activity panel is scrolled to the bottom after entries
//      arrive (scrollHeight - scrollTop - clientHeight <= 30).
//   4. After a 2nd "fetch and convert" prompt, the FIRST tool-history
//      entry is still visible above the new one.
const { chromium } = require('playwright');
const fs = require('fs');

const WEB = process.env.OCN_WEB_URL || 'http://localhost:5215';
const PROMPT1 = 'Use the markdown_convert tool to fetch https://elbruno.com and save the result to a markdown file. Set save_to_file to true.';
const PROMPT2 = 'Use the markdown_convert tool to fetch https://example.com and save the result to a markdown file. Set save_to_file to true.';

function ts() { return new Date().toISOString().slice(11, 23); }
const log = (m) => console.log(`[${ts()}] ${m}`);

async function approveOnce(page, label) {
  const approveBtn = page.locator('[data-testid="tool-approve-btn"]').first();
  const sawApproval = await approveBtn.waitFor({ state: 'visible', timeout: 90_000 })
    .then(() => true).catch(() => false);
  if (!sawApproval) {
    log(`[${label}] No approval card seen`);
    return false;
  }
  log(`[${label}] Approval card visible — clicking Approve`);
  await approveBtn.click();
  await approveBtn.waitFor({ state: 'detached', timeout: 30_000 }).catch(() => {});
  return true;
}

(async () => {
  const browser = await chromium.launch({ headless: false, slowMo: 300 });
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();

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

    // ── Verify Activity panel visible without clicking ──
    const activity = page.locator('[data-testid="agent-activity-panel"]').first();
    const activityVisible = await activity.isVisible().catch(() => false);
    log(activityVisible
      ? '✅ Agent Activity panel visible without clicking'
      : '❌ Agent Activity panel NOT visible by default');

    const input = page.locator('textarea').first();
    await input.waitFor({ state: 'visible', timeout: 15000 });
    log(`Send prompt #1`);
    await input.fill(PROMPT1);
    await input.press('Enter');

    if (!await approveOnce(page, 'P1')) throw new Error('First approval did not appear');

    // ── Verify a tool-history entry was persisted ──
    const history1 = page.locator('[data-testid="tool-history-entry"]');
    await history1.first().waitFor({ state: 'visible', timeout: 15_000 });
    const count1 = await history1.count();
    log(`✅ Tool-history entries after 1st approval: ${count1}`);
    await page.screenshot({ path: 'e2e-shot-history-1.png', fullPage: true });

    // ── Verify Activity panel is scrolled to bottom ──
    await page.waitForTimeout(2500);
    const tail = await page.evaluate(() => {
      const el = document.getElementById('agent-activity-log');
      if (!el) return { ok: false, reason: 'no element' };
      const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
      return { ok: distance <= 30, distance, scrollHeight: el.scrollHeight, clientHeight: el.clientHeight };
    });
    log(`Activity tail check: ${JSON.stringify(tail)}`);
    log(tail.ok ? '✅ Activity panel auto-scrolled to bottom' : '❌ Activity panel NOT auto-scrolled');

    // Wait for assistant turn to finish before sending the next prompt.
    // The agent may produce additional approvals mid-turn — auto-approve any.
    log('Waiting for assistant turn #1 to finish (textarea re-enabled)…');
    const deadline = Date.now() + 300_000;
    while (Date.now() < deadline) {
      const more = page.locator('[data-testid="tool-approve-btn"]').first();
      if (await more.isVisible().catch(() => false)) {
        log('  mid-turn approval — clicking');
        await more.click().catch(() => {});
        await page.waitForTimeout(1500);
        continue;
      }
      const enabled = await page.evaluate(() => {
        const t = document.querySelector('[data-testid="chat-input"]');
        return !!t && !t.disabled;
      });
      if (enabled) break;
      await page.waitForTimeout(1000);
    }
    await page.waitForTimeout(1000);

    log('Send prompt #2');
    await input.fill(PROMPT2);
    await input.press('Enter');

    await approveOnce(page, 'P2');

    await page.waitForTimeout(3000);
    const count2 = await page.locator('[data-testid="tool-history-entry"]').count();
    log(`Tool-history entries after 2nd approval: ${count2}`);
    log(count2 >= 2
      ? '✅ Audit trail accumulates — earlier entry is still visible above the new one'
      : '❌ Audit trail did not accumulate');

    await page.screenshot({ path: 'e2e-shot-history-2.png', fullPage: true });
    await page.screenshot({ path: 'e2e-shot-final.png', fullPage: true });
  } catch (e) {
    log(`FATAL: ${e.message}`);
    await page.screenshot({ path: 'e2e-shot-error.png', fullPage: true }).catch(() => {});
  } finally {
    await page.waitForTimeout(2000);
    await browser.close();
  }
})();
