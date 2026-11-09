# Architecture

## Overview

A modular monolith: one deployable web app plus one worker process, sharing a single PostgreSQL
database and a layered set of class libraries with explicit interfaces between them.

```
Couples.Web ─────┬──> Couples.Application ──> Couples.Domain
                 ├──> Couples.Infrastructure ─┘ (implements Application interfaces)
                 └──> Couples.AI ──> Couples.Application
Couples.Worker ──┬──> Couples.Application
                 └──> Couples.Infrastructure
```

| Project | Responsibility | May depend on |
|---------|----------------|---------------|
| Couples.Domain | Entities, value objects, domain rules | nothing |
| Couples.Application | Use cases, ports (interfaces such as `IClock`), authorization and consent rules, flag names | Domain |
| Couples.Infrastructure | EF Core `CouplesDbContext`, PostgreSQL, adapters for Application ports | Application, Domain |
| Couples.AI | `ICommunicationCoach` and providers (milestone 06) | Application |
| Couples.Web | Blazor UI, HTTP endpoints, composition root | Application, Infrastructure, AI |
| Couples.Worker | Scheduled/background jobs | Application, Infrastructure |

Each library exposes a single `AddXxx` extension for dependency injection; hosts compose them.

## Runtime

- **UI**: Blazor Web App with interactive server rendering and MudBlazor. Mobile-first and
  accessible; no third-party font or analytics requests from the page.
- **Data**: PostgreSQL via Npgsql EF Core, default schema `couples`, migrations history table
  `couples.__ef_migrations_history`. Entities and migrations start in milestone 02.
- **Feature flags**: `Microsoft.FeatureManagement`, configured under `FeatureManagement` and
  evaluated server-side. `Phase2JointChat` is `false` in base configuration and every flagged
  backend path must check it, not only the UI.
- **Health**: `/health` is liveness only; `/health/ready` runs checks tagged `ready`
  (currently the database).

## Privacy boundaries (applies to every milestone)

- Workspace membership never grants access to another partner's private reflections or drafts.
- Authorization and consent are enforced in Application services and data queries, not only in
  the UI.
- Every sensitive entity carries an owner or workspace scope, timestamps
  (`Entity.CreatedAt`/`UpdatedAt`) and a documented retention rule.
- Logs, telemetry, notifications, search and analytics never contain message bodies,
  reflections, AI transcripts or safety flags.

## Configuration and secrets

- Secrets come from environment variables or `dotnet user-secrets`; none are committed.
- `appsettings.Development.json` holds only the local Docker Compose credential, which is
  bound to `127.0.0.1` and has no use outside a developer machine.

## Testing

- `Couples.UnitTests`: domain and application logic, no I/O.
- `Couples.IntegrationTests`: in-memory host via `WebApplicationFactory<Program>` in the
  Production environment, with an intentionally unreachable database so tests never depend on
  local state. Database-backed tests arrive with migrations in milestone 02.
