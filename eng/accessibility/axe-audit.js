const { chromium } = require('playwright');
const fs = require('fs'); const path = require('path');
const BASE = process.env.BASE_URL || 'http://127.0.0.1:5080/'; const MAIL = process.env.MAIL_DIR || require('path').join(__dirname, '../../src/Couples.Web/.mail');
const OUT = process.argv[2]; const PW = 'correct-horse-battery';
const sleep = ms => new Promise(r => setTimeout(r, ms));
function latestLinkFor(email) {
  const files = fs.readdirSync(MAIL).map(f => path.join(MAIL, f)).sort();
  for (const f of files.reverse()) { const h = fs.readFileSync(f, 'utf8'); if (h.includes(email)) return h.match(/href="([^"]+)"/)[1].replace(/&amp;/g, '&'); }
  throw new Error('no mail for ' + email);
}
async function account(browser, email) {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, ...(global.CTXOPTS || {}) }); watch(ctx); const p = await ctx.newPage();
  await p.goto(BASE + 'Account/Register');
  await p.fill('#Input\\.Email', email); await p.fill('#Input\\.Password', PW); await p.fill('#Input\\.ConfirmPassword', PW);
  await p.click('button[type=submit]'); await p.waitForURL(/RegisterConfirmation/);
  await p.goto(latestLinkFor(email)); console.log(email, 'confirm page:', (await p.textContent('body')).includes('Thank you for confirming'));
  await p.goto(BASE + 'Account/Login');
  await p.fill('#Input\\.Email', email); await p.fill('#Input\\.Password', PW); await p.click('button[type=submit]');
  await p.waitForLoadState('networkidle'); return p;
}
async function login(browser, email) {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, ...(global.CTXOPTS || {}) }); watch(ctx); const p = await ctx.newPage();
  await p.goto(BASE + 'Account/Login'); await p.fill('#Input\\.Email', email); await p.fill('#Input\\.Password', PW);
  await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); return p;
}
async function send(a, text) {
  await a.goto(BASE + 'messages/new'); await a.waitForSelector('text=Private draft.'); await a.fill('textarea', text);
  await a.click('button:has-text("Preview")'); await a.waitForSelector('text=Check before you send');
  await a.click("text=I've read this and I want to share it"); await a.click('button:has-text("Send to my partner")'); await a.waitForSelector('h1:has-text("Sent")');
}
const AXE = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
const cspIssues = [];
function watch(ctx) {
  ctx.on('page', p => wire(p));
  ctx.pages().forEach(wire);
}
function wire(p) {
  p.on('console', m => { const t = m.text(); if (/Content Security Policy|Refused to/i.test(t)) cspIssues.push(p.url() + ' :: ' + t.slice(0, 200)); });
  p.on('pageerror', e => cspIssues.push(p.url() + ' :: pageerror ' + e.message.slice(0, 200)));
}
async function audit(page, label, results) {
  await page.waitForLoadState('networkidle'); await sleep(800);
  await page.evaluate(AXE);
  const r = await page.evaluate(() => axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] } }));
  for (const v of r.violations) results.push({ page: label, id: v.id, impact: v.impact, help: v.help, count: v.nodes.length, target: v.nodes[0].target.join(' '), html: v.nodes[0].html.slice(0, 160) });
  console.log(label.padEnd(28), 'violations:', r.violations.length, 'incomplete:', r.incomplete.length);
}
(async () => {
  const b = await chromium.launch(); const stamp = Date.now(); const OUT = process.argv[2];
  // 1. Real CSP: full flow with the policy enforced.
  const a = await account(b, `ka${stamp}@example.test`); const p = await account(b, `kb${stamp}@example.test`);
  await a.goto(BASE + 'workspace'); await a.waitForSelector('text=Create workspace'); await a.fill('input[type=text]:visible', 'Our place'); await a.click('button:has-text("Create workspace")');
  await a.waitForSelector('button:has-text("Create invite link")'); await a.click('button:has-text("Create invite link")');
  await a.waitForSelector('input[readonly]'); const link = await a.inputValue('input[readonly]');
  await p.goto(link); await p.click('button:has-text("Join")'); await p.waitForURL(/workspace/); await sleep(500);
  await a.goto(BASE + 'messages/new'); await a.waitForSelector('text=Private draft.'); await a.fill('textarea', 'Can we talk about the weekend?');
  await a.click('button:has-text("Suggest kinder wording")'); await a.waitForSelector('text=Use this wording');
  await a.click('button:has-text("Preview")'); await a.waitForSelector('text=Check before you send');
  await a.click("text=I've read this and I want to share it"); await a.click('button:has-text("Send to my partner")'); await a.waitForSelector('h1:has-text("Sent")');
  await p.goto(BASE + 'messages'); await p.click('a:has-text("Open")'); await p.waitForSelector('text=Your private thoughts');
  const receivedUrl = p.url();
  await p.goto(BASE + 'exercises'); await p.waitForLoadState('networkidle'); await p.click('button:has-text("Start"), a:has-text("Start")'); await p.waitForURL(/attempt/); const attemptUrl = p.url();
  await p.goto(BASE + 'safety'); await p.waitForSelector('button:has-text("Start")'); await p.click('button:has-text("Start")'); await sleep(500);
  await p.goto(BASE + 'settings/pace'); await p.waitForSelector('text=Take a break');
  console.log('CSP violations / page errors in the enforced flow:', cspIssues.length); cspIssues.slice(0, 10).forEach(x => console.log('  ', x));

  // 2. Keyboard: first Tab lands on the skip link, which moves focus to <main>.
  await p.goto(BASE + 'workspace'); await p.waitForLoadState('networkidle'); await sleep(500);
  await p.keyboard.press('Tab');
  console.log('first tab stop:', await p.evaluate(() => document.activeElement.textContent.trim()));
  await p.screenshot({ path: OUT + '/k1-skip.png' });
  await p.keyboard.press('Enter'); await sleep(300);
  console.log('after Enter focus is on:', await p.evaluate(() => document.activeElement.id || document.activeElement.tagName), '| url:', p.url().replace(BASE, '/'));
  await p.keyboard.press('Tab'); console.log('next tab stop:', await p.evaluate(() => document.activeElement.outerHTML.slice(0, 80)));

  // 3. axe-core audit (policy bypassed only so the audit script can be injected).
  global.CTXOPTS = { bypassCSP: true };
  const results = [];
  const anon = await (await b.newContext({ viewport: { width: 1280, height: 900 }, bypassCSP: true })).newPage();
  for (const path of ['', 'support', 'privacy', 'privacy-notice', 'terms', 'Account/Login', 'Account/Register', 'Account/ForgotPassword']) { await anon.goto(BASE + path); await audit(anon, '/' + path, results); }
  const q = await login(b, `kb${stamp}@example.test`);
  for (const [label, url] of [['/workspace', BASE + 'workspace'], ['/intake', BASE + 'intake'], ['/exercises', BASE + 'exercises'], ['/exercises/attempt', attemptUrl], ['/messages', BASE + 'messages'], ['/messages/received', receivedUrl], ['/messages/new', BASE + 'messages/new'], ['/settings/pace', BASE + 'settings/pace'], ['/safety', BASE + 'safety'], ['/pilot', BASE + 'pilot'], ['/feedback', BASE + 'feedback'], ['/Account/Manage', BASE + 'Account/Manage'], ['/Account/Manage/PersonalData', BASE + 'Account/Manage/PersonalData']]) {
    await q.goto(url); await audit(q, label, results);
  }
  fs.writeFileSync(OUT + '/axe.json', JSON.stringify(results, null, 1));
  const byRule = {}; for (const r of results) { (byRule[r.id] ||= { impact: r.impact, help: r.help, pages: [], ex: r.target + ' | ' + r.html }).pages.push(r.page + '(' + r.count + ')'); }
  for (const [id, v] of Object.entries(byRule)) console.log(`\n${id} [${v.impact}] ${v.help}\n  pages: ${v.pages.join(', ')}\n  e.g. ${v.ex}`);
  await b.close();
})().catch(e => { console.error('FAIL', e.message); process.exit(1); });
