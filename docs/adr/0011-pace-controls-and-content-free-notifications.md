# ADR-0011: Pace controls and content-free notifications

- Status: Accepted
- Date: 2026-11-09

## Context
Milestone 08 adds replies between partners. The playbook asks for pace controls, quiet hours,
pause and mute, notification metadata without message text, and no typing pressure or
read-receipt coercion.

## Decision
- No typing indicators and no read receipts. The recipient's "opened" time is stored for their
  own "New" marker only and is never part of any sender-facing view.
- Pause holds delivery: while a user is paused, their partner's preview and send return
  `PartnerPaused` and nothing is written; the draft stays. The partner sees only that a pause is
  active and when it ends. Pauses last at most 30 days and end automatically.
- Quiet hours and mute affect notices only, never delivery, and are not visible to the partner.
  Quiet hours are stored as local times plus an IANA time zone.
- Notifications are in-app rows with a kind and a message id. Their text is fixed per kind and
  never derived from content. They are created in the same transaction as the delivery, removed
  if the message is withdrawn, and purged after 90 days.
- Private notes on a message reuse `PrivateReflection` with an optional message link.

## Consequences
A partner can tell a pause is on, which is deliberate so a message isn't left waiting without
explanation. Email or push notices can be added later behind the same rules once an email
provider is chosen. The "New" marker is metadata about the recipient that must stay out of any
future sender-facing feature.
