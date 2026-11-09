// 320px reflow check (WCAG 1.4.10): reports any page that scrolls sideways at 320 CSS pixels.
// Usage: node eng/accessibility/reflow-check.js <confirmed account email> (password correct-horse-battery, Development instance).
const { chromium } = require('playwright');
const BASE = 'http://127.0.0.1:5080/'; const email = process.argv[2];
(async () => {
  const b = await chromium.launch(); const ctx = await b.newContext({ viewport: { width: 320, height: 640 } }); const p = await ctx.newPage();
  const check = async (path) => { await p.goto(BASE + path); await p.waitForLoadState('networkidle'); await p.waitForTimeout(600);
    const r = await p.evaluate(() => { const w = document.documentElement.clientWidth; const wide = [...document.querySelectorAll('body *')].filter(e => { const x = e.getBoundingClientRect(); return x.width > 0 && x.right > w + 1 && getComputedStyle(e).position !== 'fixed'; }).slice(0, 3).map(e => e.tagName + '.' + (e.className?.toString?.() || '').slice(0, 40));
      return { sw: document.documentElement.scrollWidth, w, wide }; });
    console.log((r.sw > r.w ? 'OVERFLOW ' : 'ok       ') + '/' + path, r.sw, r.w, r.wide.join(' | ')); };
  for (const path of ['', 'support', 'privacy', 'privacy-notice', 'terms', 'Account/Login', 'Account/Register']) await check(path);
  await p.goto(BASE + 'Account/Login'); await p.fill('#Input\\.Email', email); await p.fill('#Input\\.Password', 'correct-horse-battery'); await p.click('button[type=submit]'); await p.waitForLoadState('networkidle');
  for (const path of ['workspace', 'intake', 'exercises', 'messages', 'messages/new', 'settings/pace', 'safety', 'pilot', 'feedback', 'admin/pilot', 'admin/exercises', 'Account/Manage', 'Account/Manage/PersonalData']) await check(path);
  await b.close();
})();
