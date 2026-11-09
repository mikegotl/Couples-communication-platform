# Security and privacy test suite

Run everything with `dotnet test` (Docker is needed for the PostgreSQL-backed tests). CI runs the
same suite on every push and pull request, plus the vulnerability audit, secret scan and CodeQL.

| Area | Tests | What they prove |
|------|-------|-----------------|
| HTTP hardening | `Security/HttpHardeningTests` | CSP with a fresh nonce per response, framing, sniffing, referrer, permissions and cache headers; `__Host-` Secure HttpOnly cookies; antiforgery required; account posts rate limited with Retry-After; oversized bodies rejected |
| Startup guards | `Security/StartupGuardTests` | Production refuses a database connection without TLS and a key ring without a certificate; every interactive route has a page title |
| Abuse limits | `Security/MessageRateLimitTests` | 10 messages per hour per person, nothing written when limited, the partner is unaffected, the draft can be sent later |
| Data boundaries | `Privacy/PrivacyBoundaryTests`, `Intake/IntakeBoundaryTests`, `Exercises/ExerciseBoundaryTests` | Owner-only access to private data, consent to exact text, revocation, membership changes, no text or tokens in logs |
| Async exchange | `Messaging/AsyncExchangeTests`, `Messaging/DeliveryLifecycleTests` | Content-free notifications, no read receipts, pause, quiet hours, idempotent sending |
| Safety | `Safety/SafetyPathwayTests` | Unsafe joint flows held; nothing visible to the partner; no logs or audit |
| AI coach | `Coaching/CoachingTests`, unit `Coaching/*` | Validation of model output, caps, fail-closed, no secrets in config, live provider off |
| Classification | `Privacy/ClassificationTests` | Every entity has a privacy classification and is in the data inventory |
| Retention | `Privacy/RetentionTests` | Retention periods are applied |
| Deletion | `Privacy/DeletionSweepTests` | After deletion no column in the model holds the user's id except documented evidence; an account restored from a backup is erased again |
| Smoke | `SmokeTests` | Private pages require sign-in; health endpoints |

Manual checks per release: the accessibility audit (`docs/accessibility/`), the clinical review
checklist (`docs/safety/clinical-review.md`) and a review of this threat model.
