# Threat model

Scope: the web app, worker, PostgreSQL database and AI gateway as of milestone 10. Method:
assets and actors first, then STRIDE per boundary. Review this when hosting is chosen
(milestone 11) and before Phase 2 joint chat (milestone 12).

## Assets
| Asset | Why it matters |
|-------|----------------|
| Private reflections, intake notes, exercise answers, drafts | The most sensitive data: what a person writes before deciding to share |
| Safety check answers and safety mode | Could put someone at risk if their partner saw them |
| Shared messages | Seen by both partners; must reach only the intended partner and be withdrawable |
| Accounts and sessions | Access to all of the above |
| Consent ledger and audit events | Evidence of what was shared and when |
| Data Protection key ring, database credentials, AI gateway key | Unlock everything else |

## Actors
- **The other partner**, possibly controlling or abusive, who may share a device, know or guess
  the password, read notifications or watch for changes in the app's behaviour. This is the
  actor the product is most shaped around.
- An outside attacker on the internet (credential stuffing, XSS, CSRF, scraping).
- A curious or compromised operator or hosting provider.
- The AI model (output is untrusted) and anyone who can influence its input (prompt injection
  through a partner's message).
- A compromised dependency or CI action.

## Threats and mitigations (STRIDE)

| # | Threat | Mitigation | Tested by |
|---|--------|------------|-----------|
| S1 | Partner signs in as the user (known/guessed password) | 12+ character passwords, lockout after 5 failures, MFA and passkeys available, sign-in rate limit | Identity defaults; `HttpHardeningTests` |
| S2 | Credential stuffing | 10 account posts/min per address, lockout | `HttpHardeningTests.AccountFormPostsAreRateLimitedPerClient` |
| S3 | Session theft | `__Host-` Secure HttpOnly cookies, 8h sliding expiry, stamp recheck every 5 min, HSTS | `HttpHardeningTests.CookiesAreHostOnlySecureAndHttpOnlyInProduction` |
| T1 | CSRF on form posts | Antiforgery on every form, SameSite cookies, `form-action 'self'` | `HttpHardeningTests.FormPostWithoutAntiforgeryTokenIsRejected` |
| T2 | XSS through user text or AI output | Razor encodes all output, no raw HTML, nonce CSP without `unsafe-eval`, coach output validated | `HttpHardeningTests` (CSP), `PromptAndValidatorTests` |
| T3 | Clickjacking | `frame-ancestors 'none'`, `X-Frame-Options: DENY` | `HttpHardeningTests.PagesSendSecurityHeaders` |
| T4 | Sharing text other than what was approved | Fingerprint of previewed text must match at send; consent ledger | `PrivacyBoundaryTests.SharingRequiresConsentToTheExactPreviewedText` |
| R1 | Disputes over what was shared | Consent ledger with fingerprint and time; audit events (ids only) | `PrivacyBoundaryTests` |
| I1 | Partner reads private data by id (IDOR) | Every query scoped to the owner in the service layer; partner access only to shared, unrevoked messages | `PrivacyBoundaryTests`, `IntakeBoundaryTests`, `ExerciseBoundaryTests`, `AsyncExchangeTests` |
| I2 | Partner learns about safety answers or safety mode | Silent hold, no notices, no visible change; no logs or audit | `SafetyPathwayTests` |
| I3 | Private text in logs, notifications, telemetry | Ids-only logging, fixed notification text | `PrivacyBoundaryTests.PrivateTextAndInviteTokensNeverReachLogs`, `AsyncExchangeTests`, `SafetyPathwayTests` |
| I4 | Browser or proxy caches private pages | `Cache-Control: no-store` on dynamic responses | `HttpHardeningTests.PagesSendSecurityHeaders` |
| I5 | Referrer leaks the site to a quick-exit or support destination | `Referrer-Policy: no-referrer` | `HttpHardeningTests` |
| I6 | Data sent to an AI provider without agreement | Live provider off until owner approves provider and DPA | `CoachingTests` |
| I7 | Database or backup exposure | TLS required to DB, key ring encrypted with a certificate, platform encryption at rest (hosting requirement) | `DatabaseTransportSecurity`, startup checks |
| I8 | Data kept after deletion | Erasure across every user-id column; restored accounts re-erased | `DeletionSweepTests` |
| D1 | Request floods | Per-address request limit, body and header size limits | `HttpHardeningTests` |
| D2 | Message flooding a partner | 10 messages per hour per person; pause controls | `MessageRateLimitTests`, `AsyncExchangeTests` |
| D3 | AI cost exhaustion | Per-user and global daily caps, fail closed | `CoachServiceTests`, `CoachingTests` |
| E1 | Non-admin reaches admin pages | Role check on pages; roles granted only from configuration | `SmokeTests.PrivatePagesRequireSignIn` |
| E2 | Prompt injection makes the coach act on instructions | Nonce-delimited input, canary leak check, schema validation, no tools | `PromptAndValidatorTests` |
| J1 | Joint chat reachable before it is approved | `Phase2JointChat` off by default: pages 404 before authentication, every service method checks the flag; also needs both sign-offs in configuration and per-workspace eligibility | `JointChatHttpTests`, `JointChatPilotTests.WithTheFlagOff...`, `TheDefaultConfigurationKeepsThePilotOff` |
| J2 | One partner pressures the other into a live chat, or keeps it going | Fresh consent from both per chat; declining needs no reason; either leaves at once; pauses; turn-taking; idle and total time limits | `JointChatPilotTests`, `JointChatRulesTests` |
| J3 | Joint chat used to threaten or demean | Local moderation: contempt held for rephrasing (not stored), risk phrases end the chat with support shown only to the writer; no text in logs | `JointChatPilotTests.ModerationHolds...`, `NoChatTextOrModerationOutcomeReachesTheLogs` |
| J4 | Partner infers the other's safety check from joint chat behaviour | Held user's chats end with the same neutral text as any ending; invitations to them lapse like any unanswered invitation; no audit or log of the reason | `JointChatPilotTests.ASafetyHoldEndsTheChatSilentlyAndHidesInvitations` |
| E3 | Compromised dependency or leaked secret | NuGet audit, CodeQL, Dependabot, secret scan in CI | CI |

## Residual risks and open items
- A partner with the user's unlocked device can read whatever the user can. Quick exit, no
  notices in safety mode and no-store caching reduce this but can't prevent it.
- Per-address limits need the real client address behind a proxy (milestone 11 hosting).
- No application-level field encryption (ADR-0013).
- Accessibility and safety copy still need human review (screen reader pass, clinician review).
- A penetration test before any public launch.
- Joint chat moderation is a short, local phrase list: it will miss things and occasionally hold
  harmless text. It is a guard rail for a supervised pilot, not a safety system; the clinical
  review decides whether it is enough (`docs/safety/joint-chat-review.md`).
- Joint chat live updates are in-process, so the web app must run as a single replica.
