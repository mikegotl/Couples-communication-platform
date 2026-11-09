# Progress

Milestones follow the build playbook, one feature branch and PR each. `main` is merged by a human.

| # | Milestone | Status |
|---|-----------|--------|
| 01 | Repository and architecture | Done (PR #1) |
| 02 | Identity and partner workspace | Done (PR #2) |
| 03 | Privacy, consent and data boundaries | In review |
| 04 | Intake and concern mapping | Not started |
| 05 | Guided private reflection exercises | Not started |
| 06 | AI coaching abstraction and safeguards | Not started |
| 07 | Draft, preview, approve and deliver | Not started |
| 08 | Asynchronous partner responses | Not started |
| 09 | Safety and support pathways | Not started |
| 10 | Accessibility, security and data lifecycle | Not started |
| 11 | Private pilot readiness and observability | Not started |
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
