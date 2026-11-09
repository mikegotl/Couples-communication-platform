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
- **Data**: PostgreSQL via Npgsql EF Core, default schema `couples`, migrations in
  `Couples.Infrastructure/Persistence/Migrations`, history table `couples.__ef_migrations_history`.
  Domain entities generate their own UUIDv7 ids; aggregates that can be changed concurrently use
  PostgreSQL `xmin` as a concurrency token.
- **Identity**: ASP.NET Core Identity with `Guid` keys, cookie authentication, confirmed email
  required, lockout, MFA and passkeys (ADR-0006). The scaffolded account UI lives in
  `Couples.Web/Components/Account` and renders as static server-side pages.
- **Workspaces**: `RelationshipWorkspace` aggregate (Domain) owns `WorkspaceMembership` and
  `Invitation`. `IWorkspaceService` (Application) is implemented in Infrastructure; every
  method takes the acting user's id and checks active membership in the query itself.
  Interactive pages resolve the user id with `CurrentUser` and never pass ids from the URL as
  proof of access.
- **Feature flags**: `Microsoft.FeatureManagement`, configured under `FeatureManagement` and
  evaluated server-side. `Phase2JointChat` is `false` in base configuration and every flagged
  backend path must check it, not only the UI.
- **Health**: `/health` is liveness only; `/health/ready` runs checks tagged `ready`
  (currently the database).

## Privacy boundaries (applies to every milestone)

See [docs/privacy/data-inventory.md](docs/privacy/data-inventory.md) for every entity's
classification, readers and retention, and ADR-0007 for sharing and consent.

- Workspace membership never grants access to another partner's private reflections or drafts.
- Authorization and consent are enforced in Application services and data queries, not only in
  the UI.
- Every sensitive entity carries an owner or workspace scope, timestamps
  (`Entity.CreatedAt`/`UpdatedAt`) and a documented retention rule.
- Logs, telemetry, notifications, search and analytics never contain message bodies,
  reflections, AI transcripts or safety flags. A test captures all logs from a full flow and
  fails if any private text or invite token appears.
- Services: `IPrivateReflectionService` and `IMessagingService` (owner-scoped), `IPersonalDataService`
  (export and deletion workflow), `IRetentionService` (purge by rule). The worker's
  `PrivacyMaintenanceWorker` retries pending deletions every 5 minutes and applies retention
  every 6 hours.

## Configuration and secrets

- Secrets come from environment variables or `dotnet user-secrets`; none are committed.
- `appsettings.Development.json` holds only the local Docker Compose credential, which is
  bound to `127.0.0.1` and has no use outside a developer machine.

## Testing

- `Couples.UnitTests`: domain and application logic, no I/O.
- `Couples.IntegrationTests`:
  - HTTP smoke tests via `WebApplicationFactory<Program>` in the Production environment with an
    unreachable database (pages, auth redirects, flags, migration drift check).
  - Database tests against a disposable PostgreSQL container (Testcontainers) migrated with the
    real migrations: workspace, invitation and isolation rules, including concurrency.
