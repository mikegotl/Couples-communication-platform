# ADR-0008: Private intake lives outside workspace scope

- Status: Accepted
- Date: 2026-11-09

## Context
Milestone 04 asks each partner to choose the topics they want to work on (finances, trust,
infidelity, parenting, conflict, intimacy, custom) and write private notes, without one
partner's choices reaching the other. Even "your partner has finished intake" or "your partner
picked infidelity" would be a disclosure.

## Decision
- `IntakeProfile` (one per user) and `ConcernSelection` are keyed by `OwnerUserId` only. They have
  no workspace id, so no workspace query, partner view or future notification can join to them.
- `IIntakeService` takes only the acting user's id; another user's topic id returns `NotFound`.
- The topic catalog is code (`ConcernCategory.All`) seeded into `concern_categories` by
  migrations so selections have a foreign key. Topics with a content warning (infidelity,
  conflict, intimacy) can be added only after the warning is acknowledged; the time of that
  acknowledgement is stored.
- Intake is optional: users can skip and resume, edit notes after finishing, or erase it and
  start over. Export includes it; account deletion erases it.
- The workspace page shows only the viewer's own intake status.
- Logs record user id, status and topic count only.

## Consequences
Later milestones that want to use intake topics (for example, to suggest exercises) must read
them through `IIntakeService` for the acting user. Any feature that shows partners something in
common based on intake needs a new, explicit consent step and its own ADR.
