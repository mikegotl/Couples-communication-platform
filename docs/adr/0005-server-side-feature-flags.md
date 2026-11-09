# ADR-0005: Server-side feature flags, Phase 2 off

- Status: Accepted
- Date: 2026-11-09

## Context
Phase 2 guided joint chat is a clinician-reviewed pilot that must never be silently enabled.

## Decision
Use Microsoft.FeatureManagement with flags evaluated on the server. Flag names live in
`Couples.Application.FeatureFlags`. `Phase2JointChat` defaults to `false` in base configuration;
enabling it requires independent clinical and security sign-off and per-workspace eligibility
(milestone 12). Both UI and backend paths check the flag.

## Consequences
An integration test asserts the flag is off by default. Pilot enrollment and per-workspace
targeting are added in milestones 11 and 12.
