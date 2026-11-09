# Decisions

Architecture decision records live in [docs/adr](docs/adr). Add a new ADR for any decision
that is costly to reverse; never edit an accepted ADR's decision, supersede it instead.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](docs/adr/0001-modular-monolith-on-dotnet-10.md) | Modular monolith on .NET 10 LTS | Accepted |
| [0002](docs/adr/0002-blazor-web-app-with-mudblazor.md) | Blazor Web App with MudBlazor | Accepted |
| [0003](docs/adr/0003-postgresql-with-ef-core.md) | PostgreSQL with EF Core | Accepted |
| [0004](docs/adr/0004-provider-neutral-ai-coach.md) | Provider-neutral AI coach | Accepted |
| [0005](docs/adr/0005-server-side-feature-flags.md) | Server-side feature flags, Phase 2 off | Accepted |

## Open decisions (owner)

- Working brand, jurisdiction, legal/privacy counsel and launch market.
- AI provider and data-processing agreement (retention and training controls verified first).
- Licensed clinician reviewers for exercises and high-risk flows.
- Whether a therapist portal is a separate later product (no therapist access by default).
- Hosting target for staging (milestone 11); nothing is deployed before then.
