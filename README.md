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

See [ARCHITECTURE.md](ARCHITECTURE.md) and [DECISIONS.md](DECISIONS.md).

## Prerequisites

- .NET SDK 10.0.100 or later (pinned with `global.json`, `latestFeature` roll-forward)
- Docker with Compose v2 (for local PostgreSQL)

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

The Development connection string in `src/Couples.Web/appsettings.Development.json` matches the
local-only Compose defaults. To change them, copy `.env.example` to `.env` (git-ignored) and
override `ConnectionStrings__Couples` through environment variables or `dotnet user-secrets`.
Never commit real credentials.

## Endpoints

| Path | Purpose |
|------|---------|
| `/` | Anonymous home page |
| `/health` | Liveness (process up; no dependency checks) |
| `/health/ready` | Readiness (includes PostgreSQL connectivity) |

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
