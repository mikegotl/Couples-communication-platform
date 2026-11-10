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
| [0006](docs/adr/0006-identity-and-workspace-membership.md) | Identity and workspace membership | Accepted |
| [0007](docs/adr/0007-consent-ledger-and-per-message-sharing.md) | Consent ledger and per-message sharing | Accepted |
| [0008](docs/adr/0008-private-intake-outside-workspace-scope.md) | Private intake lives outside workspace scope | Accepted |
| [0009](docs/adr/0009-versioned-exercises-and-clinician-review.md) | Versioned exercises with clinician review metadata | Accepted |
| [0010](docs/adr/0010-coach-safeguards-and-gated-live-provider.md) | Coach safeguards and an owner-gated live provider | Accepted |
| [0011](docs/adr/0011-pace-controls-and-content-free-notifications.md) | Pace controls and content-free notifications | Accepted |
| [0012](docs/adr/0012-private-safety-check-and-silent-joint-flow-hold.md) | Private safety check and a silent hold on joint flows | Accepted |
| [0013](docs/adr/0013-security-baseline-and-encryption.md) | Security baseline, encryption and abuse limits | Accepted |
| [0014](docs/adr/0014-private-pilot-observability-and-staging.md) | Private pilot enrollment, scrubbed observability, Azure UK staging | Accepted |
| [0015](docs/adr/0015-phase-2-joint-chat-behind-flag-and-sign-off.md) | Phase 2 joint chat behind a flag, two sign-offs and per-workspace eligibility | Accepted (not activated) |
| [0016](docs/adr/0016-google-and-apple-sign-in.md) | Google and Apple sign-in, linked by verified email only | Accepted |

## Open decisions (owner)

- Working brand, jurisdiction, legal/privacy counsel and launch market.
- Transactional email provider (needed before any non-development deployment; see ADR-0006).
- AI provider and data-processing agreement (retention and training controls verified first).
- Licensed clinician reviewers for exercises and high-risk flows, including the milestone 09
  safety check (see docs/safety/clinical-review.md).
- Jurisdiction-specific support resources for the launch market (`Safety:Resources`).
- Whether a therapist portal is a separate later product (no therapist access by default).
- Approval to apply the Azure staging Terraform (`deploy/azure`), which costs money, and whether
  to give it a public address. Hosting itself is decided: Azure, UK South (2026-11-09, ADR-0014).
- Legal review of the pilot terms and privacy notice; DPIA; ICO registration
  (`docs/operations/pilot-readiness.md`).
- Google OAuth client and Apple Services ID and key, if sign-in with them is wanted
  (`docs/operations/external-sign-in.md`); Apple needs a paid developer membership.
- The pilot invitation code, and the named incident roles (`docs/operations/incident-response.md`).
- Independent clinical and security sign-off of joint chat before any workspace is made eligible
  (`docs/safety/joint-chat-review.md`); until then `Phase2JointChat` stays off everywhere.
