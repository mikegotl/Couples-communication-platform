# ADR-0016: Google and Apple sign-in

- Status: Accepted
- Date: 2026-11-10

## Context
The owner asked for Google and Apple sign-in alongside email and password accounts. Accounts in
this app guard very private writing, so a convenience sign-in must not become a way into someone
else's account.

## Decision
- **Providers**: ASP.NET Core's Google handler and the aspnet-contrib Apple handler, each
  registered only when all of its settings (`Authentication:Google:*`, `Authentication:Apple:*`)
  are present. No setting, no button, no callback endpoint. Secrets come from configuration
  only; Apple's client secret is a short-lived JWT the app signs with the `.p8` key.
- **Matching an existing account by email** happens only when both sides vouch for the address:
  the provider says it is verified (Apple always does; Google per account via `email_verified`)
  and the existing account's email is confirmed. An unconfirmed account is never linked,
  because whoever registered it may not own the address (pre-account-takeover).
- **New accounts** from a verified provider address start confirmed and sign straight in. If
  the provider shares no verified address, the person types one and confirms it by email, as
  for a password account.
- **Two-factor authentication still applies** to provider sign-ins (`bypassTwoFactor: false`).
- **CSP**: browsers apply `form-action` to the redirect after a form post, so the configured
  providers' sign-in origins are added to `form-action`, and nothing else changes.
- Errors from the callback are shown on the callback page, not passed through the strict status
  cookie, which is dropped after Apple's cross-site form post. Provider error text is never shown.

## Consequences
Signing in with Google or Apple tells that provider the person uses BridgeTogether; the privacy
notice says so. People who only use a provider have no password; they can add one under
Account, Password, and deleting their account needs no password, as before. Apple needs a paid
developer membership and an https domain. Setup steps: `docs/operations/external-sign-in.md`.
