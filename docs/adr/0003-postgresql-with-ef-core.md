# ADR-0003: PostgreSQL with EF Core

- Status: Accepted
- Date: 2026-11-09

## Context
Relational data with strict ownership and workspace scoping, encryption at rest, and mature
managed hosting options.

## Decision
PostgreSQL 17 via Npgsql EF Core with code-first migrations in Couples.Infrastructure, default
schema `couples`. Local development uses Docker Compose bound to `127.0.0.1`. Row scoping is
enforced in Application queries; database-level row security may be added later.

## Consequences
Migrations are reviewed in PRs. Encryption at rest depends on the hosting provider and is
verified in milestone 10.
