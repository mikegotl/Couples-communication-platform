# Progress

Milestones follow the build playbook, one feature branch and PR each. `main` is merged by a human.

| # | Milestone | Status |
|---|-----------|--------|
| 01 | Repository and architecture | In review |
| 02 | Identity and partner workspace | Not started |
| 03 | Privacy, consent and data boundaries | Not started |
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

GitHub Actions has not run yet at the time of writing; see the PR checks.

**Not in scope / next**: no entities, identity or migrations yet (milestone 02).
