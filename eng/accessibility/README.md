# Accessibility audit script

Signs up two test accounts against a local Development instance, walks the main flows with the
Content Security Policy enforced (reporting any violations), checks the skip link with the
keyboard, then runs axe-core (WCAG 2.0/2.1/2.2 A and AA rules) on 21 pages.

```bash
dotnet run --project src/Couples.Web --urls http://127.0.0.1:5080   # Development, local Postgres
npm install --no-save playwright axe-core
npx playwright install chromium
mkdir -p out && node eng/accessibility/axe-audit.js out
```

Results are written to `out/axe.json`. Record findings and their triage in
`docs/accessibility/`. Automated rules catch roughly a third of WCAG issues; the manual checks
listed in the latest audit are still needed.

`reflow-check.js` signs in with an existing confirmed account and reports any page that scrolls
sideways at 320 CSS pixels (WCAG 1.4.10): `node eng/accessibility/reflow-check.js you@example.test`.
