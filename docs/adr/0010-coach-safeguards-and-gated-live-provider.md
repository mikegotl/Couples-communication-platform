# ADR-0010: Coach safeguards and an owner-gated live provider

- Status: Accepted
- Date: 2026-11-09

## Context
ADR-0004 makes the coach provider-neutral. The vendor and its data processing terms are still an
owner decision, yet the coach must be testable now, must not leak or send anything when a
provider fails, and must resist prompt injection from either partner's text.

## Decision
- The contract (`ICommunicationCoach`, `ICoachUsageStore`) lives in Couples.Application so other
  modules depend on it without referencing Couples.AI; Couples.AI holds the implementation.
- Model access goes through an internal `ICoachModelClient`. Shipped clients: `Fake` and
  `Gateway`, a generic HTTPS adapter for a gateway the owner runs, which receives instructions,
  data, a JSON schema and limits and returns text and token usage. A vendor-specific client can
  replace it later without changing the coach.
- The live client is selected only when `Coaching:Provider=Gateway`, the gateway is fully
  configured with an https endpoint, and `Coaching:ApprovedForUserContent=true`. The default is
  `Disabled`; Development uses `Fake`. Keys come from secrets, never committed settings, and a
  test enforces this.
- Every output is untrusted: it must parse as the schema, stay within limits and pass content
  checks, or it is discarded. All failures return a fixed "Unavailable" message and leave the
  draft unchanged. The coach cannot send messages.
- Spending is capped per user and service-wide per day in PostgreSQL. Only counts are stored.

## Consequences
The coach is usable in Development and tests with no network. Turning on a live model is a
configuration change that the owner makes after choosing a provider. Content checks are
keyword-based and can reject acceptable suggestions; that costs a retry or an "Unavailable",
which is preferred over showing a harmful one. The local safety screen is a stopgap until
milestone 09.
