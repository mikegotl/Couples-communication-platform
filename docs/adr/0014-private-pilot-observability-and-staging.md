# ADR-0014: Private pilot enrollment, scrubbed observability, and Azure UK staging

- Status: Accepted
- Date: 2026-11-09

## Context
Milestone 11 prepares a private pilot of up to 60 people: enrollment with consent to versioned
terms and a privacy notice, published legal and support content, monitoring, feedback, a
deployable build, and incident and rollback plans. The data is special category, so monitoring
must not become a second copy of it. The owner chose Azure in a UK region for staging.

## Decision
- **Enrollment gate.** With `PilotEnrollmentRequired` on, a signed-in person who hasn't agreed
  to the current terms and privacy notice versions is sent to `/pilot` for every page except
  the open ones (home, account pages, `/pilot`, `/terms`, `/privacy-notice`, `/support`,
  `/privacy`, static files). Interactive connections and posts get 403 rather than a redirect.
  `/support` is always reachable. Enrollment records the two document versions, needs an
  invitation code (only its SHA-256 is configured, compared in fixed time), and is capped at
  `Pilot:MaxParticipants`. Changing a document version makes everyone agree again. Leaving is
  one button and keeps the person's data until they delete it.
- **Legal and support content** lives in `docs/legal/*.md`, is embedded in the web assembly at
  build time and rendered with Markdig with raw HTML disabled. The reviewed file in the repository
  is exactly what users read (a test compares them). The drafts say "pending legal review".
- **Observability.** OpenTelemetry metrics (counts only: messages shared, withdrawn and blocked,
  coach requests, enrollments, feedback, deletions) and traces, exported over OTLP only when
  `Observability:OtlpEndpoint` is set. Before export, spans lose URL, query, client address and
  user agent and are named by route template; logs lose exception messages and structured
  values, and emails and invitation tokens in text are masked. Container logs go to the platform
  log store with 30-day retention.
- **Operations overview** for admins shows aggregate counts only and hides any count below 5,
  so a small pilot can't be used to infer one person's activity.
- **Feedback** is anonymous by default (area, optional 1-5 rating, up to 2,000 characters).
  Contact details are linked only if the person ticks the box. Five per person per day. Kept 365
  days, included in export and removed on deletion.
- **Build and staging.** Web and worker images on .NET chiseled (distroless) bases, running as a
  non-root user, built in CI and never pushed by CI. Staging is Terraform for Azure UK South:
  Container Apps (one web replica), PostgreSQL Flexible Server on a private network with TLS
  required and a 14-day restore window, Key Vault for secrets and a vault-generated key-ring
  certificate, managed identities, and no public ingress unless the owner turns it on. The
  Terraform is validated in CI and has never been applied. A Docker Compose rehearsal runs the
  same images with production settings behind a TLS proxy.
- **Forwarded headers** are trusted only from configured proxy networks (or all proxies, when
  the app is reachable only through one), so HTTPS detection, Secure cookies and per-client rate
  limits work behind the ingress.

## Consequences
- The pilot cannot start without the owner's legal and clinical reviews, an email provider and
  approval to apply the Terraform (`docs/operations/pilot-readiness.md`).
- Monitoring can show that something is failing but never what anyone wrote. Debugging relies on
  ids, counts, routes and exception types.
- One web replica limits scale; it is ample for 60 people. Scaling out needs sticky sessions or
  Azure SignalR Service.
- Rotating the key-ring certificate signs everyone out once.

## Alternatives considered
- **Application Insights SDK** instead of OpenTelemetry: ties the code to one vendor; OTLP keeps
  the app provider-neutral and Azure Monitor can ingest OTLP.
- **Separate migration job**: cleaner with several replicas; with one replica, migrating at
  start-up is simpler and the worker now retries a failed run on its next tick.
- **App Service** instead of Container Apps: also viable; Container Apps runs the same images as
  the rehearsal and CI, with Key Vault secret references and managed identity built in.
