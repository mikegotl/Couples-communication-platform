# ADR-0013: Security baseline, encryption and abuse limits

- Status: Accepted
- Date: 2026-11-09

## Context
Milestone 10 asks for secure headers, CSRF and session protection, input validation, rate
limits, encryption configuration, export and deletion, a backup policy, security scanning and a
threat model. The app holds intimate private writing, and one realistic attacker is the other
partner, who may know the user's password habits or share a device.

## Decision
- **Headers.** Every response carries a nonce-based Content Security Policy (`script-src 'self'`
  plus a per-request nonce for the framework's import map; no `unsafe-eval`; `frame-ancestors
  'none'`; `form-action 'self'`), `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy:
  no-referrer` (so quick exit and support links don't reveal this site), a restrictive
  Permissions-Policy, COOP/CORP `same-origin`, `Cache-Control: no-store` on dynamic responses, and
  no `Server` header. `style-src` allows inline styles because MudBlazor sets style attributes.
- **Cookies and CSRF.** Outside Development the auth and antiforgery cookies use the `__Host-`
  prefix, are Secure and HttpOnly; antiforgery is SameSite=Strict, auth is SameSite=Lax (so
  emailed links work). Antiforgery is enforced on all form posts. Sessions last 8 hours sliding;
  the security stamp is rechecked every 5 minutes, so a password change ends other sessions
  quickly.
- **Rate limits.** Per client address: 10 account form posts per minute (sign in, register,
  reset), 600 requests per minute overall; health checks are exempt. Per person: at most 10
  messages to a partner in any rolling hour (`RateLimited`, draft kept), which also damps rapid
  escalation. The AI coach keeps its own daily caps (ADR-0010).
- **Input limits.** Request bodies over 1 MB and headers over 32 KB are rejected; SignalR
  messages are capped at 64 KB; every text field has a domain length limit and is rendered
  encoded (no raw HTML anywhere).
- **Encryption in transit.** Outside Development the web app and worker refuse to start unless
  the database connection uses TLS (`SSL Mode=VerifyFull|VerifyCA|Require`). HSTS and HTTPS
  redirection are on.
- **Encryption at rest.** The Data Protection key ring (cookies, antiforgery, account tokens) is
  stored in PostgreSQL so all instances share it, and outside Development it must be encrypted
  with a certificate (`DataProtection:CertificatePath`) or the app won't start. Database storage
  and backups must be encrypted by the hosting platform (managed PostgreSQL with encrypted
  storage and encrypted backups); this is a hosting requirement for milestone 11.
- **No application-level field encryption yet.** Encrypting text columns in the app would need
  a key management service and key rotation that depend on the hosting decision, and protects
  mainly against database-only compromise that platform encryption plus least-privilege access
  already narrows. Revisit when hosting is chosen; the data inventory marks the candidate
  columns.
- **Per-page service scopes.** Interactive pages inherit `ScopedPage` (an `OwningComponentBase`),
  so each page has its own DbContext instead of sharing one for the whole connection.
- **Scanning.** CI runs the NuGet vulnerability audit and a secret scan on every push; CodeQL
  (security-extended) runs on pushes, PRs and weekly; Dependabot proposes dependency updates.

## Consequences
- Production needs: a TLS database connection, a key-ring certificate, encrypted storage and
  backups, and a reverse proxy that forwards the client address (configure forwarded headers
  for the chosen host, or per-address limits see only the proxy).
- Inline scripts can't be added without the nonce; third-party scripts and fonts are blocked by
  design.
