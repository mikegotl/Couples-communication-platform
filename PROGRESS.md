# Progress

Milestones follow the build playbook, one feature branch and PR each. `main` is merged by a human.

| # | Milestone | Status |
|---|-----------|--------|
| 01 | Repository and architecture | Done (PR #1) |
| 02 | Identity and partner workspace | Done (PR #2) |
| 03 | Privacy, consent and data boundaries | Done (PR #3) |
| 04 | Intake and concern mapping | Done (PR #4) |
| 05 | Guided private reflection exercises | Done (PR #5) |
| 06 | AI coaching abstraction and safeguards | Done (PR #6) |
| 07 | Draft, preview, approve and deliver | Done (PR #7) |
| 08 | Asynchronous partner responses | Done (PR #8) |
| 09 | Safety and support pathways | Done (PR #9; clinical review pending) |
| 10 | Accessibility, security and data lifecycle | Done (PR #10) |
| 11 | Private pilot readiness and observability | In review (legal and clinical review, email provider and apply approval pending) |
| 12 | Phase 2 joint chat, flag OFF | Not started |

## Milestone 01: Repository and architecture

**Delivered**

- `Couples.slnx` with Couples.Web, Domain, Application, Infrastructure, AI, Worker, UnitTests
  and IntegrationTests; dependency direction enforced by project references.
- Shared build settings (`Directory.Build.props`): .NET 10, nullable, warnings as errors,
  recommended analyzers, NuGet vulnerability audit. SDK pinned in `global.json`.
- Blazor Web App (interactive server) with MudBlazor layout and an anonymous home page that
  states the product is not therapy or a crisis service. No third-party font or CDN requests.
- `/health` (liveness) and `/health/ready` (PostgreSQL via EF Core health check).
- Server-side feature management with `Phase2JointChat` set to `false`.
- `CouplesDbContext` (schema `couples`, no entities yet), `IClock`, DI extension per layer.
- Worker host with a content-free heartbeat.
- Docker Compose PostgreSQL 17 bound to `127.0.0.1`, `.env.example`.
- GitHub Actions: restore, Release build, tests with TRX upload, vulnerable package check,
  Compose validation.
- README, ARCHITECTURE, DECISIONS and ADRs 0001 to 0005.

**Verification** (2026-11-09, fresh clone of the branch, Linux, .NET SDK 10.0.114, Docker 29.9.2)

| Command | Result |
|---------|--------|
| `docker compose up -d` | `couples-postgres` started, healthy |
| `dotnet restore Couples.slnx` | Succeeded |
| `dotnet build Couples.slnx -c Release` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Couples.slnx -c Release` | UnitTests 3/3 passed, IntegrationTests 4/4 passed |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any project |
| `dotnet run --project src/Couples.Web` (Development) | `/` 200, `/health` 200 Healthy, `/health/ready` 200 Healthy |
| `dotnet run --project src/Couples.Worker` (Development) | Started, logged "Couples worker started" |

GitHub Actions passed on PR #1 (Build and test, Security checks).

**Not in scope / next**: no entities, identity or migrations yet (milestone 02).

## Milestone 02: Identity and partner workspace

**Delivered**

- ASP.NET Core Identity with `Guid` keys in the `couples` schema, using the Blazor template's
  account UI restyled without third-party CSS: register, confirm email, log in, log out,
  forgot/reset password, resend confirmation, change email/password, MFA with authenticator and
  recovery codes, passkeys, personal data download and delete.
- Policy: confirmed email required, lockout after 5 failures, 12-character minimum password,
  8-hour sliding cookie, secure cookies outside Development.
- Removed the template's on-screen confirmation link (anyone could confirm any address) and the
  "user not found" response on the confirmation page (account enumeration).
- Email senders: Development pickup folder (`src/Couples.Web/.mail/`, git-ignored); elsewhere a
  no-op that logs a warning without addresses, links or codes. Provider choice is an open decision.
- Domain: `RelationshipWorkspace`, `WorkspaceMembership`, `Invitation`, `Result`/`DomainError`.
- `IWorkspaceService`: create, view, invite, preview, accept, revoke, leave. Membership is
  checked in every query; a workspace you are not in is "not found".
- Invitations: random 256-bit token shown once, SHA-256 hash stored, single use, 72 h default
  lifetime, one pending at a time, revoked when the inviter leaves, `xmin` concurrency so only one
  person can accept.
- One active workspace per user (partial unique index) and two members per workspace.
- Pages: `/workspace` (create, invite with copy button, cancel invite, leave with confirmation)
  and `/invite/{token}` (preview, join or decline). Both require sign-in.
- Migration `InitialIdentityAndWorkspaces`; Development applies it on startup.
- ADR-0006; README, ARCHITECTURE, DECISIONS updated.

**Verification** (2026-11-09, fresh clone of the branch, .NET SDK 10.0.114, Docker 29.9.2)

| Command | Result |
|---------|--------|
| `dotnet restore` / `dotnet build -c Release` | Succeeded, 0 warnings, 0 errors |
| `dotnet test -c Release` | UnitTests 13/13, IntegrationTests 26/26 passed (PostgreSQL 17 via Testcontainers) |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| Browser walkthrough (Playwright, Development, Compose PostgreSQL) | See below |

Security tests (database-backed) cover: unrelated account cannot read, invite for, or leave a
workspace; foreign and nonexistent workspace ids return the same error; token reuse by another
user or the same user; expired, revoked, replaced and malformed tokens; inviter accepting their
own invite; third member; already-in-a-workspace; leaver loses access immediately; leaving revokes
the leaver's invites; six concurrent accepts of one token admit exactly one; only the token hash
is stored. HTTP tests cover sign-in redirects for workspace pages and a migration drift check.

Browser walkthrough: A registered, confirmed email from the pickup folder, signed in, created a
workspace and an invite link; B registered separately, opened the link, joined; both saw the
partner as joined; C (a third account) opening the same link was refused and saw no workspace;
an anonymous visitor to `/workspace` was redirected to `/Account/Login`.

**Known limits / next**: invite links are bearer secrets until accepted (documented in the UI
and ADR-0006); account pages use the template's layout rather than MudBlazor components; no email
provider yet. Next: milestone 03 (privacy, consent and data boundaries).

## Milestone 03: Privacy, consent and data boundaries

**Delivered**

- `[Privacy(classification, retention)]` on every persisted type and a data inventory
  (`docs/privacy/data-inventory.md`); tests fail if either is missing for an entity.
- Owner-only `PrivateReflection` and `DraftMessage` with `IPrivateReflectionService` and the
  draft half of `IMessagingService`. Another user's id behaves like a nonexistent id.
- Explicit per-message sharing: `ShareAsync` requires the SHA-256 fingerprint of the previewed
  text, writes `SharedMessage` + `ConsentRecord` + `AuditEvent` in one transaction, once per draft
  (unique index + `xmin`). Sender can revoke: recipient loses access, text is erased, consent
  withdrawn. Recipients read only while both partners are active members.
- `IPersonalDataService`: export of the user's own data (wired into Identity's download, now
  without the authenticator key) and a deletion workflow (request, erase content rows, delete
  account) used by the Delete page and retried by the worker.
- `IRetentionService` and `PrivacyMaintenanceWorker`: purge closed invitations, discarded
  drafts and empty workspaces after 30 days; audit events and completed deletion requests after
  400 days.
- Workspace creator no longer has a foreign key to the account, so a creator can delete their
  account and the partner keeps the workspace.
- `/privacy` page in plain language; home page links to sign-up and privacy.
- Migration `PrivacyConsentAndContent`; ADR-0007.

**Verification** (2026-11-09, fresh clone of the branch)

| Command | Result |
|---------|--------|
| `dotnet build -c Release` | Succeeded, 0 warnings, 0 errors |
| `dotnet test -c Release` | UnitTests 21/21, IntegrationTests 43/43 passed |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| Browser check (Development) | Export downloads `account` + `app` sections without the authenticator key; deleting A's account signs A out, A can no longer log in, B keeps the workspace with no partner |

Gate tests (database-backed): partner cannot read, update, delete or list another partner's
reflection; cannot read, edit, discard or share another partner's draft; sharing fails on a stale
or missing fingerprint and before a partner joins; consent record and audit event written on
share; concurrent shares of one draft deliver once; outsider cannot read a shared message by id;
revoke hides and erases; recipient leaving or sender leaving ends access, including in export;
a member who left cannot share old drafts; exports contain only the requester's private data;
captured logs contain no reflection text, draft text, workspace name or invite token; deletion
erases every content row and the account while the partner's data remains; retention purges only
records past their rule.

Notifications do not exist yet (milestone 08); the data inventory records the no-content rule
they must follow.

## Milestone 04: Intake and concern mapping

**Delivered**

- Topic catalog `ConcernCategory` (finances, trust, infidelity, parenting, conflict, intimacy,
  custom) seeded by migration. Infidelity, conflict and intimacy carry content warnings that must
  be acknowledged before the topic is added; the conflict warning points to emergency services
  and domestic abuse support.
- Owner-only `IntakeProfile` and `ConcernSelection` with `IIntakeService`: add topics (up to 12,
  up to 5 custom), editable private notes (up to 4,000 characters), remove, finish, skip and
  resume, start over. No workspace scope at all (ADR-0008).
- `/intake` page ("My topics" in the app bar) and a "Your private topics" card on the workspace
  page showing only the viewer's own status. Not-therapy disclaimer on the intro.
- Intake added to personal data export and to the deletion workflow; data inventory updated.
- Fix: MudBlazor dialog and snackbar providers moved into interactive pages
  (`InteractiveProviders`). They were in the static layout, so the "Leave workspace"
  confirmation from milestone 02 never appeared.
- Fix: on narrow screens the app bar links wrap instead of running off-screen.
- Migration `IntakeAndConcerns`; ADR-0008.

**Verification** (2026-11-09, fresh clone of the branch)

| Command | Result |
|---------|--------|
| `dotnet build -c Release` | Succeeded, 0 warnings, 0 errors |
| `dotnet test -c Release` | UnitTests 32/32, IntegrationTests 52/52 passed |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| Browser check (Development, 390px wide) | A and B in one workspace. A adds Trust, sees the infidelity warning before it is added, acknowledges it, adds a custom topic, saves a note and finishes; the note survives reload. B's workspace and intake pages contain none of A's topics, notes or status. B skips, sees the resume prompt, then completes their own intake. A's export contains A's intake. "Leave workspace" dialog now renders. |

Gate tests (database-backed): each partner completes intake independently; a partner cannot
update or remove another partner's topic by id; the partner's workspace view contains nothing
about intake; export contains only the requester's intake; logs contain no topic labels or notes;
skip, start over and account deletion erase intake; concurrent first selections create one
intake; unknown topics and unacknowledged warnings are rejected; `/intake` requires sign-in.

## Milestone 05: Guided private reflection exercises

**Delivered**

- Versioned `ExerciseDefinition` (content, SHA-256 content hash, clinician review status, reviewer,
  time, notes) and owner-only `ExerciseAttempt` + `ExerciseResponse`.
- Four original exercises written for clinician review, with no diagnostic or assessment content:
  "Hear it back" (reflective listening), "Name it: feelings and needs", "Pause before you reply"
  and "Reach back after a rough moment" (repair). The repair and conflict-adjacent copy points to
  emergency and domestic abuse support.
- `IExerciseService`: list with own progress, start or resume, save a step and move in one call,
  back, finish, history, delete. Required steps block moving forward; optional steps can be
  skipped. Attempts stay on their version when a new one is published.
- `/exercises` and `/exercises/attempt/{id}`: step counter and progress bar, focus moves to each
  step's heading, choice questions grouped in fieldsets with legends, "Saved at" announced via
  `aria-live`, "Save and exit", read-only summary with "Edit answers" and "Delete".
- Admin: Identity roles enabled; `Admin` granted from `Administration:AdminEmails` at startup;
  `/admin/exercises` shows every version's review status, reviewer, notes, step count, publish
  time and whether the stored content hash matches code, and records reviews (audited).
- `Exercises:RequireClinicianApproval` (default off) limits new attempts to approved versions.
- Export and account deletion include exercises; data inventory updated. Migration
  `GuidedExercises`; ADR-0009.

**Verification** (2026-11-09, fresh clone of the branch)

| Command | Result |
|---------|--------|
| `dotnet build -c Release` | Succeeded, 0 warnings, 0 errors |
| `dotnet test -c Release` | UnitTests 45/45, IntegrationTests 65/65 passed |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| Browser check (Development, 390px wide) | Exercise list shows the "awaiting review" note. Starting "Hear it back" moves focus to the step heading; an empty required step is blocked with a message; "Save and exit" returns to the list, which shows "Continue". In a new browser session "Continue" resumes at step 3 with the partial answer; Back keeps earlier answers; finishing shows a summary with the skipped optional step. Another account opening the attempt URL sees "Exercise not found". A non-admin opening `/admin/exercises` gets Access denied. The configured admin sees four versions pending review, records "Clinician approved" with a reviewer name, and users then see "Reviewed by a licensed clinician". |

Gate tests (database-backed): catalog publishes four versions pending review that match code;
progress and current step survive across requests and resume; required steps cannot be skipped;
finishing starts a fresh attempt next time; a partner cannot read, change, finish, delete or list
another partner's attempt and gets their own attempt instead; export, logs and deletion keep
answers private; attempts stay on their version when a new version is published; reviews are
validated, recorded and audited and can gate new attempts; retired versions cannot be started
but existing attempts resume; published content hashes are pinned; catalog copy avoids clinical
and diagnostic terms.

**Owner decisions**: engage a clinician reviewer, and decide whether to turn on
`Exercises:RequireClinicianApproval` for the pilot.

## Milestone 06: AI coaching abstraction and safeguards

**Delivered**

- `ICommunicationCoach` (Application/Coaching): rephrase a draft or suggest a reply, returning a
  structured result (outcome, one to three suggestions with a short explanation, a fixed
  user-facing message). The coach never sends anything; it has no access to messaging.
- Providers behind `ICoachModelClient`: `Disabled` (default), `Fake` (deterministic, in-process,
  used in Development and tests) and `Gateway`, a configurable HTTPS adapter for a model gateway
  the owner runs. The gateway is used only when it is fully configured (https endpoint, model,
  key from configuration or secrets) and `Coaching:ApprovedForUserContent` is true.
- Prompt (`coach-v1`) follows the playbook's AI behavior specification: a writing assistant, not a
  therapist, judge, mediator or crisis responder; no diagnosis, labels, blame, guessing motives,
  pressure to forgive, or advice to stay or leave; safety concerns and off-topic requests are
  flagged instead of answered.
- Injection defences: input normalized and stripped of zero-width, bidi and other hidden
  characters; user and partner text wrapped in markers carrying a fresh random nonce; a random
  canary in the instructions; strict JSON schema validation of output with length limits, and
  rejection of output that leaks the canary or markers, contains links, emails, phone numbers,
  role or script tags, hidden characters, or diagnostic, blaming or therapy language.
- Reliability: per-attempt and overall timeouts, up to three attempts with exponential backoff
  and jitter for timeouts, throttling and server errors, one retry for invalid output, and no
  retry for configuration errors. Every failure ends in a safe "Unavailable" result that leaves
  the draft untouched; exceptions never reach the caller.
- Cost caps: per-user daily requests (atomic conditional upsert), and service-wide daily request
  and token caps. Usage (`coaching_usage`, counts only) is exported, erased with the account and
  purged after 90 days.
- Local safety screen before any provider call: text suggesting self-harm, violence or fear for
  safety returns support information and is not sent. Milestone 09 replaces this with the full
  safety pathway.
- Privacy-safe telemetry: meter `Couples.Coaching` (requests by outcome, rejected outputs, tokens,
  duration) tagged with enums only; logs carry request id, user id, task, prompt version,
  provider, outcome, attempts and elapsed time, never text.
- Migration `CoachingUsage`; ADR-0010. No UI yet: the coach is wired into drafting in milestone 07.

**Verification**: see the PR description for the commands run on a fresh clone and their results.

Gate tests: outages, slow providers, unexpected errors and invalid output all fall back to
Unavailable with no suggestions and nothing sent; prompt injection in the draft or the partner
message stays inside the nonce markers and spoofed end markers cannot close them; hidden
characters are removed before the provider sees the text; output leaking the canary or
instructions, or containing diagnoses, blame, links or markup, is rejected; refusals and safety
flags map to Declined and SupportSuggested; the safety screen and daily caps stop calls before
the provider; the gateway sends the expected request and is never used without approval and full
configuration; per-user caps hold under 20 concurrent requests; logs never contain drafts or
output; the repository contains no provider keys and committed settings keep the gateway
unapproved.

**Owner decisions**: choose the AI provider and data processing agreement, run or choose the
gateway, then set `Coaching:Provider=Gateway`, the gateway settings (key via secrets, never
committed) and `Coaching:ApprovedForUserContent=true`. Review the caps.

## Milestone 07: Draft, preview, approve and deliver

**Delivered**

- `/messages`: the author's private drafts, messages from the partner, and messages sent (with
  Withdraw, which hides the message and erases its text after a confirmation that says a read
  copy can't be recalled). "Messages" is in the top navigation and on the workspace page.
- `/messages/new` and `/messages/draft/{id}`: write a private draft; optionally "Suggest kinder
  wording" (shown only when a coach provider is enabled), which saves the draft, asks the coach,
  and lists suggestions with explanations. "Use this wording" copies one into the editor for the
  author to edit, with Undo. Suggestions are never stored, applied automatically or shared.
- Preview: `IMessagingService.PreviewShareAsync` returns exactly the text the partner will see,
  the workspace name and the fingerprint to approve; it needs a partner and writes nothing. The
  page renders it with the same card the partner sees. Sending needs a ticked confirmation and
  sends the previewed fingerprint; if the draft changed since the preview nothing is sent.
- Idempotent delivery: a repeated or concurrent confirmation of the same draft returns the
  original message and records no second message, consent record or audit event. Confirming again
  after a withdrawal returns the withdrawn message and never re-delivers it.
- `IDraftAssistant` (Couples.AI) reads the draft through the messaging service, so only the
  owner can get suggestions, and only for an unsent draft.
- The coach now fails closed if the usage store is unreachable (no provider call) and still
  returns suggestions if recording tokens fails.
- Fixed in the browser check: creating a draft no longer triggers a Blazor navigation, which had
  re-created the page and used the circuit's database context concurrently.

**Verification**: see the PR description for the commands run on a fresh clone and their results.

Gate tests (database-backed): preview shows exact text and creates no message, consent or audit
rows; preview is owner-only and needs a partner; editing after preview blocks delivery until a new
preview; repeated and concurrent confirmations return one delivery with one consent record and one
audit event; a wrong approval is rejected; confirming after withdrawal never re-delivers;
suggestions are owner-only, change nothing, share nothing and stay out of logs; a disabled coach
reports itself unavailable.

## Milestone 08: Asynchronous partner responses

**Delivered**

- Inbox (`IInboxService`): received messages with a "New" marker for the recipient only, whether
  the recipient has private notes, a reply in progress or has replied. Opening a message
  (`/messages/received/{id}`) clears its marker and its notice; the sender is never told.
- Private notes on a received message, stored as the recipient's own `PrivateReflection` linked
  to the message; prompts suggest noting what they heard, how they feel and what they'd need
  before replying. The copy says there's no rush and no need to reply.
- Optional reply: "Start a reply" creates a private reply draft (`InReplyToMessageId`) that goes
  through the same preview and confirmation as any message. The coach suggests replies with the
  partner's message as context. The partner sees the reply together with what they sent.
- Pace (`/settings/pace`, `IPaceService`): pause new messages for 1 day to 2 weeks (delivery is
  held and the sender's draft kept; the partner sees only that a pause is on and until when),
  quiet hours in the user's time zone (notices held until they end, daylight saving handled), and
  mute (no notices; messages still arrive). Pauses end by themselves.
- Notifications (`INotificationService`): in-app only, kind and message id only, fixed wording,
  shown in the "Messages (n)" link once past quiet hours, marked read when the inbox or the
  message is opened, removed when the message is withdrawn, purged after 90 days.
- Export and account deletion include pace settings and notifications; deleting an account
  removes notices about its messages and unlinks (but keeps) the partner's own notes.
- Migration `AsyncResponsesAndPace`; ADR-0011.

**Verification**: see the PR description for the commands run on a fresh clone and their results.

Gate tests (database-backed): a full exchange (send, content-free notice, open without a read
receipt, private notes, reply through preview and approval, reply notice, thread view) with no
text in logs and no notes in the sender's export; notification rows have metadata columns only;
quiet hours hold notices but not messages; mute skips notices; pause blocks preview and delivery
without writing anything, is visible to the partner only as an end time, and expires; withdrawal
removes the notice and closes the inbox item; senders and outsiders cannot open, annotate or
reply to the recipient's messages; account deletion erases pace and notices but keeps the
partner's notes. Unit tests cover quiet hours across midnight and daylight saving, validation and
pause limits.

**Not yet**: email or push notifications (they need the email provider decision and would carry
the same fixed, content-free wording).

## Milestone 09: Safety and support pathways

**Delivered**

- Private safety check (`/safety`): four short questions, versioned and hashed placeholder
  content marked "Pending licensed clinician review". Results: no concerns, concern (support
  options only) or may be unsafe. A separate question shows self-harm support.
- Silent hold: "may be unsafe" or safety mode holds the user's own sharing (`SafetyHold`, nothing
  written) and stops their notices. Their partner's messages still arrive and nothing the partner
  can see changes. `ISafetyGate` gives future joint features the same rule. The user can answer
  again, delete their answers or turn safety mode off at any time.
- "Leave quickly" on every page (replaces the history entry with a neutral site) and a public
  `/support` page with configurable resources and honest limits ("this app can't contact anyone").
- Non-coercive copy: no pressure to talk, reconcile, stay or leave; safety-focused alternatives
  for threatening situations; no automated emergency claims (unit-tested).
- Messages, the inbox count and the coach's safety response link to support. Safety data is in
  the owner's export and erased with the account; never logged, audited or sent to the coach.
- Migration `PersonalSafety`; ADR-0012; [clinical review checklist](docs/safety/clinical-review.md).

**Verification**: see the PR description for the commands run on a fresh clone and their results.

Gate tests (database-backed): unsafe answers hold preview and send without writing anything,
and deleting the answers lifts the hold; concern answers never block; safety mode is invisible to
the partner (their send succeeds, their pace, workspace and export are unchanged) while the held
user gets no notice but still has the message; the workspace gate closes when either partner is
held; no answers or levels in logs and no audit events; validation, owner-only reads, export and
erasure. Unit tests cover scoring, completeness, the pinned content hash and copy rules.

**Not yet**: the manual clinical review part of the gate (needs the owner's clinician), and
jurisdiction-specific resources (needs the launch market decision).

## Milestone 10: Accessibility, security and data lifecycle

**Delivered**

- Security headers on every response: nonce-based CSP (no `unsafe-eval`, no framing), no
  referrer, nosniff, Permissions-Policy, COOP/CORP, `no-store` on dynamic pages, no `Server`.
- `__Host-` Secure HttpOnly cookies outside Development; antiforgery SameSite=Strict; security
  stamp rechecked every 5 minutes.
- Rate limits: 10 account form posts per minute and 600 requests per minute per client address;
  10 messages per hour per person (`RateLimited`, the draft is kept). Body, header and SignalR
  size limits.
- Encryption configuration: TLS required to the database; Data Protection key ring stored in
  PostgreSQL and certificate-encrypted outside Development (startup fails otherwise); platform
  encryption at rest required of hosting. Migration `DataProtectionKeys`.
- Deletion verified: a sweep over every user-id column in the model after deletion. It found two
  leftovers (accepted invitations and the workspace creator link), both now erased. Accounts that
  reappear after a backup restore are erased again by the worker.
- Accessibility: axe-core audit of 17 pages to WCAG 2.2 AA, 0 violations after fixes (status
  colour contrast, underlined links, a title on every page, danger button contrast); skip link,
  visible focus, reduced motion. Audit and triage in `docs/accessibility/audit-2026-11.md`.
- Fixed a server error on `/Account/Manage` for signed-in users (the "Messages (n)" link shared
  the request's DbContext with the account page). Interactive pages now get their own scope.
- Security scanning: secret scan in CI (`eng/scan-secrets.sh`), CodeQL (C#, JavaScript, Actions;
  security-extended), Dependabot for NuGet and Actions, alongside the NuGet vulnerability audit.
- Threat model, security test suite, backup policy, ADR-0013.

**Verification**: see the PR description for the commands run on a fresh clone and their results.

Gate tests (documented in `docs/security/test-suite.md`): HTTP hardening (headers, fresh nonce,
cookies, antiforgery, rate limit with Retry-After, body limit), the message rate limit, the
deletion sweep and restore re-erasure, plus every earlier privacy and safety test.

**Not yet**: a screen reader pass and zoom/reflow checks (triaged in the audit), a penetration
test before public launch, forwarded-header configuration for the chosen host.

## Milestone 11: Private pilot readiness and observability

**Delivered**

- Pilot enrollment at `/pilot`: agreement to versioned pilot terms and privacy notice, an
  invitation code (only its SHA-256 is configured; startup fails without it outside
  Development), a participant cap, re-agreement when a document changes, and leaving at any
  time. `PilotGate` keeps people who haven't joined out of the app but always lets them reach
  support, the legal pages and their account. Migration `PilotAndFeedback`.
- Content publishing: the privacy notice and pilot terms (`docs/legal`, drafts pending legal
  review) are embedded and rendered at `/privacy-notice` and `/terms`; a test checks the pages
  match the files. Footer links on every page; `/privacy` links to the notice.
- Monitoring: OpenTelemetry metrics, traces and logs over OTLP (opt-in), scrubbed of URLs,
  addresses, agents, exception messages, structured values, emails and tokens. Count-only
  metrics for sharing, withdrawal, blocked shares, coach use, enrollment, feedback and deletion.
- Feedback at `/feedback`: anonymous by default, contact only if allowed, 5 per day, kept 365
  days, in export and deletion. `/admin/pilot` shows enrollment, operations counts (under 5
  hidden) and feedback.
- Containers: non-root chiseled images for web and worker, built in CI (not pushed). A local
  staging rehearsal (`deploy/staging`) with production settings behind a TLS proxy, TLS to
  PostgreSQL and a certificate-encrypted key ring. Forwarded-header support for proxies.
- Azure UK South staging as Terraform (`deploy/azure`): Container Apps, private PostgreSQL
  Flexible Server (TLS, 14-day restore, UK geo-backup), Key Vault with a vault-generated
  key-ring certificate, managed identities, no public ingress by default. Validated in CI;
  never applied.
- Plans: incident response (severity, kill switches, UK GDPR 72-hour breach steps with safe
  participant contact), release and rollback (expand/contract migrations), pilot readiness
  checklist. ADR-0014.
- Fixes found in the rehearsal: joining the pilot failed with 400 (the form posted its
  antiforgery token twice; a test now checks every form); the worker waited 6 hours to retry
  retention after a failure at start-up (now the next 5-minute tick). The key ring certificate
  can be read in Key Vault's base64 form.

**Verification**: see the PR description for the commands run on a fresh clone and their results.

**Not yet (owner)**: legal review of the notice and terms, clinical sign-off, an email provider
(nobody can confirm an account on a deployed instance without one), approval to apply the
Terraform and to make it public, named incident roles, a screen reader pass, restore and
rollback drills. See `docs/operations/pilot-readiness.md`.

