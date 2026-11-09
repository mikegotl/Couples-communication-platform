# ADR-0001: Modular monolith on .NET 10 LTS

- Status: Accepted
- Date: 2026-11-09

## Context
A small team needs to ship a privacy-sensitive product quickly while keeping clear boundaries
between domain logic, persistence, AI integration and UI. Microservices would add operational
cost and more places for sensitive data to leak.

## Decision
Build one solution with Domain, Application, Infrastructure, AI, Web and Worker projects and
enforce dependency direction through project references. Target .NET 10 LTS; assess .NET 11
only after its supported release and dependency compatibility are confirmed. The SDK is pinned
in `global.json` with `latestFeature` roll-forward.

## Consequences
Single deployable plus a worker; boundaries are compile-time enforced. Modules can be extracted
later if scale demands it.
