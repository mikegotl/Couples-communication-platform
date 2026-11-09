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
| `/Account/Register`, `/Account/Login`, `/Account/Manage` | Accounts: sign-up with email confirmation, sign-in, password reset, MFA, passkeys |
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
tests/
  Couples.UnitTests
  Couples.IntegrationTests
docs/adr/                 Architecture decision records
```

## Working agreements

- One milestone per feature branch and pull request; `main` is protected and merged by a human.
- No production deployment, paid-service provisioning, or public access without owner approval.
- Private content never appears in logs, telemetry, notifications, or analytics.
