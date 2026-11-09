# ADR-0012: Private safety check and a silent hold on joint flows

- Status: Accepted
- Date: 2026-11-09

## Context
Milestone 09 asks for clinician-reviewed safety screening placeholders, escalation-safe UI, a
private exit, support resources and non-coercive prompts, and says not to assume every couple
should reconcile or communicate jointly. Joint flows (shared messages now, joint chat in phase 2)
can be unsafe when one partner is afraid of the other, and a visible "safety block" could itself
tip off a controlling partner.

## Decision
- A short, optional, private safety check (`SafetyContent`, versioned and hashed like exercises)
  gives a level: no concerns, concern, or may be unsafe. A separate question offers self-harm
  support. Wording is a placeholder marked "Pending licensed clinician review".
- "May be unsafe", or the user switching on safety mode, holds that user's own joint flows:
  `PreviewShareAsync` and `ShareAsync` return `SafetyHold` and write nothing. `ISafetyGate`
  exposes the same rule for future joint features (`WorkspaceAllowsJointFlowsAsync` is false if
  either member is held), so phase 2 joint chat must check it server side.
- The partner sees nothing different. Their messages are still delivered and their views,
  pace and workspace summary are unchanged; the held user simply gets no notices. A "concern"
  result only shows support options and never blocks.
- The user stays in control: they can answer again, delete their answers (which lifts the hold
  unless safety mode is on), or turn safety mode off.
- Safety answers are `PrivateSensitive`, owner only, included in the owner's export, erased with
  the account, and never logged, audited, sent to the AI coach or used in analytics.
- A "Leave quickly" link on every page replaces the current history entry with a neutral site
  (`Safety:ExitUrl`, https only). `/support` is public and lists resources from configuration plus
  two built-in, jurisdiction-neutral entries. Copy never claims the app can contact anyone or
  monitor content.

## Consequences
- The check and resources need a licensed clinician's review and jurisdiction-specific
  resources before any pilot (see `docs/safety/clinical-review.md`).
- A held user cannot send messages until they change their answers or safety mode. This is
  deliberate: it errs toward not opening joint channels when someone may be at risk.
- Quick exit cannot remove earlier history entries or the browser's own history menu; the support
  page says so and suggests private browsing.
