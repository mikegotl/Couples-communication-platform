# Couples Communication Platform

Working product name: **BridgeTogether** (placeholder).

A privacy-first, responsive web app that helps romantic partners communicate about difficult
topics through separate, guided reflection and consent-based sharing. Each partner reflects
privately with an AI writing coach; only messages a partner explicitly reviews and approves are
shared.

> BridgeTogether is an educational communication-support tool. It is **not** licensed therapy,
> diagnosis, a crisis service, or a substitute for professional couples therapy.

## Status

Early development. See [PROGRESS.md](PROGRESS.md) for the milestone tracker. Phase 2 joint chat
is behind the `Phase2JointChat` server-side flag, which is **off** by default.

## Stack

- .NET 10 (LTS), ASP.NET Core Blazor Web App (interactive server), MudBlazor
- PostgreSQL with EF Core (Npgsql)
- Microsoft.FeatureManagement for server-side flags
- xUnit unit and integration tests, GitHub Actions CI

See [ARCHITECTURE.md](ARCHITECTURE.md), [DECISIONS.md](DECISIONS.md) and the
[data inventory](docs/privacy/data-inventory.md).

## Prerequisites

- .NET SDK 10.0.100 or later (pinned with `global.json`, `latestFeature` roll-forward)
- Docker with Compose v2 (local PostgreSQL; integration tests start their own PostgreSQL container with Testcontainers)

## Getting started

```bash
git clone https://github.com/mikegotl/couples-communication-platform.git
cd couples-communication-platform

docker compose up -d          # local PostgreSQL on 127.0.0.1:5432
dotnet restore
dotnet build
dotnet test

dotnet run --project src/Couples.Web       # web app (Development profile)
dotnet run --project src/Couples.Worker    # background worker
```

In Development the app applies EF Core migrations on startup (`Database:MigrateOnStartup`) and
writes account emails (confirmation, password reset) as HTML files to `src/Couples.Web/.mail/`
instead of sending them. Open the newest file to confirm a test account. To apply migrations by
hand, or after adding one:

```bash
dotnet tool install --global dotnet-ef
dotnet ef database update -p src/Couples.Infrastructure -s src/Couples.Infrastructure
dotnet ef migrations add <Name> -p src/Couples.Infrastructure -s src/Couples.Infrastructure -o Persistence/Migrations
```

The Development connection string in `src/Couples.Web/appsettings.Development.json` matches the
local-only Compose defaults. To change them, copy `.env.example` to `.env` (git-ignored) and
override `ConnectionStrings__Couples` through environment variables or `dotnet user-secrets`.
Never commit real credentials.

## Endpoints

| Path | Purpose |
|------|---------|
| `/` | Anonymous home page |
| `/Account/Register`, `/Account/Login`, `/Account/Manage` | Accounts: sign-up with email confirmation, sign-in, password reset, MFA, passkeys, optional Google and Apple sign-in ([setup](docs/operations/external-sign-in.md)) |
| `/workspace` | Create a workspace, invite your partner, leave (signed in) |
| `/invite/{token}` | Review and accept an invitation (signed in) |
| `/intake` | Private intake: choose topics to work on and write private notes (signed in) |
| `/exercises` | Guided private exercises; progress saves after every step (signed in) |
| `/messages` | Private drafts, messages from your partner, sent messages and withdrawal (signed in) |
| `/messages/new`, `/messages/draft/{id}` | Write a draft, get optional wording suggestions, preview exactly what your partner will see, confirm and send |
| `/messages/received/{id}` | Read a message from your partner, keep private notes, start an optional reply |
| `/safety` | Private safety check, support options, safety mode, delete answers (signed in) |
| `/support` | Support resources and how quick exit works (public) |
| `/settings/pace` | Pause new messages, quiet hours, mute notifications |
| `/admin/exercises` | Exercise content and clinician review status (Admin role only) |
| `/privacy` | Plain-language summary of what is private and what is shared |
| `/privacy-notice`, `/terms` | The privacy notice and pilot terms (drafts pending legal review; public) |
| `/pilot` | Join the private pilot (agree to the terms and notice, invitation code) or leave it |
| `/feedback` | Send anonymous feedback, optionally allowing contact (signed in) |
| `/admin/pilot` | Pilot enrollment, operations counts (under 5 hidden) and feedback (Admin role only) |
| `/joint-chat`, `/admin/joint-chat` | Phase 2 joint chat and its per-workspace eligibility. **404 unless `Phase2JointChat` is on**, which it isn't anywhere by default |
| `/Account/Manage/PersonalData` | Download your data, or delete your account and all your private data |
| `/health` | Liveness (process up; no dependency checks) |
| `/health/ready` | Readiness (includes PostgreSQL connectivity) |

### AI coach

The coach is off by default (`Coaching:Provider=Disabled`). Development uses the in-process fake
provider, which sends nothing anywhere. A live model is used only through an owner-run gateway
(`Coaching:Gateway:Endpoint`, `Model`, and `ApiKey` from user secrets or environment variables,
never committed) and only once `Coaching:ApprovedForUserContent=true` is set after a provider and
data processing agreement are chosen. Daily caps are under `Coaching:` (see ADR-0010).

### Safety and support

Every page has a "Leave quickly" link to `Safety:ExitUrl` (https only). Support resources come
from `Safety:Resources` (name, description, optional contact and https link) plus two built-in,
jurisdiction-neutral entries. The safety content is a placeholder pending licensed clinician
review ([checklist](docs/safety/clinical-review.md)).

### Security configuration

Outside Development the app and worker refuse to start unless the database connection uses TLS
(`SSL Mode=VerifyFull` or `Require`) and `DataProtection:CertificatePath` (plus
`CertificatePassword`, from a secret store) points at a certificate that encrypts the key ring.
Rate limits are under `RateLimits:`. See ADR-0013, the
[threat model](docs/security/threat-model.md), the
[security test suite](docs/security/test-suite.md) and the
[backup policy](docs/operations/backup-policy.md). `./eng/scan-secrets.sh` checks tracked files
for credentials (CI runs it too).

### Private pilot

With `FeatureManagement:PilotEnrollmentRequired=true` (the default outside Development) signed-in
people must join the pilot at `/pilot` before using the app: they agree to the current pilot
terms and privacy notice and enter the invitation code. Only the code's SHA-256 is configured
(`Pilot:AccessCodeSha256`; `printf '%s' 'the-code' | sha256sum`), and the app refuses to start
without it. `Pilot:MaxParticipants` caps enrollment (60). The legal texts are
`docs/legal/*.md`, embedded at build time; bump `PilotDocuments` versions when they change so
everyone agrees again. Feedback (`FeatureManagement:FeedbackForm`) is anonymous unless the person
allows contact. Readiness: [pilot checklist](docs/operations/pilot-readiness.md).

### Phase 2 joint chat (off)

A guided, turn-based chat between partners, built for a later pilot and switched off. Using it
needs all of: `FeatureManagement:Phase2JointChat=true`, `JointChat:ClinicalSignOff` and
`JointChat:SecuritySignOff` (references to the signed reviews in
[the sign-off checklist](docs/safety/joint-chat-review.md)), and an administrator making the
workspace eligible at `/admin/joint-chat`. Do not turn it on for real participants before both
reviews are signed (ADR-0015).

### Progress and date rewards

`/progress` (and a card on the workspace page) shows a couple's shared "connection rhythm" score,
built only from messages they've shared with each other. With `FeatureManagement:DateRewards` on,
finished weeks earn shared credits they can cash in for date rewards from the catalog at
`/admin/rewards`. Development turns the flag on and seeds clearly marked sample offers
(`Rewards:SeedSampleCatalog`); Production leaves it off until real reward partners or a gift-card
provider are signed up. How the score works and where a provider plugs in: ADR-0017.

Local businesses apply at `/partners`; an admin approves them at `/admin/partners`, which creates
their offer and a private check link (`/partner/check/...`) where the business confirms a couple's
code and marks it used. "Date ideas near you" (managed at `/admin/rewards`) are suggestions with no
voucher (ADR-0018). Recruiting partners: [playbook](docs/operations/reward-partners.md).
With a Google key configured, the same section also shows real places in the couple's town from
Google Places, each linking to Google Maps (ADR-0019, off by default;
[setup](docs/operations/google-places.md)).

### Observability

Metrics (counts only, meter `BridgeTogether`), traces and logs are exported over OTLP when
`Observability:OtlpEndpoint` is set; otherwise only console logs. Telemetry is scrubbed first:
no URLs, query strings, client addresses, user agents, exception messages or structured log
values, and emails and invitation tokens are masked (ADR-0014).

### Containers and staging

`deploy/docker/web.Dockerfile` and `worker.Dockerfile` build non-root images on chiseled .NET
bases (CI builds them; nothing is pushed). `deploy/staging/compose.yaml` is a local rehearsal
with production settings, TLS to PostgreSQL, an encrypted key ring and a TLS proxy:

```bash
./deploy/staging/make-certs.sh
docker compose -f deploy/staging/compose.yaml up --build   # then https://localhost:8443
```

`deploy/azure` is Terraform for Azure UK South (Container Apps, private PostgreSQL Flexible
Server, Key Vault). It is validated in CI and **has not been applied**; see its
[README](deploy/azure/README.md). Behind a proxy or ingress set `ForwardedHeaders:Enabled=true`
and `ForwardedHeaders:KnownNetworks` (or `TrustAllProxies` when only the proxy can reach the
app). Operations: [incident response](docs/operations/incident-response.md),
[release and rollback](docs/operations/rollback-plan.md),
[backup and restore](docs/operations/backup-policy.md).

### Admin access

There is no self-service admin. List confirmed accounts under `Administration:AdminEmails`
(for example `Administration__AdminEmails__0=you@example.com` as an environment variable) and
restart the web app; it grants them the `Admin` role at startup.

## Repository layout

```
src/
  Couples.Domain          Entities and domain rules (no dependencies)
  Couples.Application     Use cases, interfaces, feature flag names
  Couples.Infrastructure  EF Core/PostgreSQL, system services
  Couples.AI              Provider-neutral AI coaching (milestone 06)
  Couples.Web             Blazor Web App + MudBlazor UI, HTTP endpoints
  Couples.Worker          Background jobs (retention, expiry, deletion)
deploy/
  docker                  Production Dockerfiles (web, worker)
  staging                 Local staging rehearsal (Compose, TLS everywhere)
  azure                   Terraform for Azure UK South staging (never applied)
tests/
  Couples.UnitTests
  Couples.IntegrationTests
docs/adr/                 Architecture decision records
```

## Working agreements

- One milestone per feature branch and pull request; `main` is protected and merged by a human.
- No production deployment, paid-service provisioning, or public access without owner approval.
- Private content never appears in logs, telemetry, notifications, or analytics.
