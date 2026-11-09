# ADR-0004: Provider-neutral AI coach

- Status: Accepted
- Date: 2026-11-09

## Context
The AI provider and its data-processing agreement are an open owner decision, and tests must not
depend on a live model.

## Decision
All AI access goes through an `ICommunicationCoach` interface in Couples.AI returning
structured, validated JSON, with a fake implementation for tests (milestone 06). No user text is
sent to any provider until the owner selects one and confirms retention and training controls.
The coach never sends messages; sending always requires explicit user confirmation in the app.

## Consequences
Swapping providers is an adapter change. Structured output validation and fallback paths are
required for every provider.
