# ADR-0015: Phase 2 joint chat behind a flag, two sign-offs and per-workspace eligibility

- Status: Accepted (built; not activated)
- Date: 2026-11-09

## Context
Phase 2 of the product is an opt-in, guided joint chat between partners. A live conversation
removes the time to reflect that Phase 1's draft-preview-send flow builds in, so it carries more
risk: pressure to join, escalation, and a partner learning about the other's safety check-in.
The playbook asks for an isolated pilot behind `Phase2JointChat` (default off) with
per-workspace eligibility, fresh consent from both partners, independent opt-out, turn-taking,
pause and exit, reviewed moderation, audit and safety gating, and no exposure to ordinary users
before independent clinical and security sign-off.

## Decision
- **Four locks, all checked in the service layer on every call.** The flag; both sign-off
  references in configuration (`JointChat:ClinicalSignOff`, `JointChat:SecuritySignOff`); an
  administrator's eligibility record for the workspace; and two current members. With the flag
  off, `/joint-chat` and `/admin/joint-chat` answer 404 before authentication runs, the nav link
  and workspace section don't render, and every service method returns "not found" without
  writing anything. Services read flags through `IFeatureFlags`, so the worker and tests see the
  same values as the web app.
- **Consent per chat.** Starting and joining each record a consent-ledger entry
  (`ConsentPurpose.JointChatSession`, subject = the chat, fingerprint = the policy version). A
  policy change bumps `JointChatPolicy.Version` and old consent no longer matches. Declining
  needs no reason; the inviter sees only that the chat ended.
- **Either can leave instantly**, which ends the chat for both. Leaving needs only the flag: no
  eligibility, membership or safety condition can block it.
- **Turns and time.** The inviter writes first; partners alternate one message at a time. After
  5 minutes without a reply either may write. Either can pause for 5, 10 or 20 minutes; nobody
  can write during a pause and only the pauser can end it early. A chat ends after 20 quiet
  minutes or 60 minutes in all; an invitation lapses after 30. Time rules are applied on the
  server whenever the chat is read or changed.
- **Moderation, local and deterministic.** Contemptuous words are held for the writer to
  rephrase and the held text is never stored. Risk phrases (shared with the coach's safety
  screen) end the chat, the text is not delivered or stored, and only the writer is shown
  support. No text goes to an AI provider for moderation. The lists are part of the clinical
  review.
- **Safety gating without disclosure.** While either partner's check-in or safety mode holds
  joint flows (`ISafetyGate`), a running chat ends and the held person sees why, privately. Their
  partner sees the same "This chat has ended" as any other ending. An invitation to a held
  person is never shown to them and lapses like any unanswered one.
- **Audit (ids only):** eligibility granted or revoked, invited, joined, and endings a person
  chose (left, declined). Endings caused by safety or moderation are not audited or logged.
- **Data.** Messages are SharedSensitive. Both partners can read the transcript during the chat
  and for 7 days after it ends; retention then deletes it. Account deletion removes every chat
  the person took part in. Export includes their chats (the partner's lines only while both
  are still in the workspace, as for received messages).
- **Live updates** use an in-process notifier, so the web app runs as one replica (as the
  staging Terraform already does).

## Consequences
- Nobody can use joint chat in any environment until the owner records both sign-offs and
  makes a workspace eligible. Production configuration keeps the flag off.
- Moderation is a guard rail, not a safety system; the clinical review decides whether it is
  sufficient for a supervised pilot.
- Scaling out would need a shared notifier (for example Azure SignalR Service or PostgreSQL
  LISTEN/NOTIFY).

## Alternatives considered
- **AI moderation**: more nuanced, but it sends intimate text to a provider before the AI
  provider and DPA decision, and adds latency and failure modes to a live conversation.
- **Free-form chat without turns**: closer to ordinary messaging, but makes rapid escalation
  easy; turn-taking and pauses mirror structured couples-communication exercises without copying
  any proprietary protocol.
- **Self-service opt-in per workspace**: simpler, but the playbook requires no exposure before
  sign-off; admin eligibility keeps the pilot to recruited, screened couples.
