# Data inventory

Every persisted application type, its classification, who can read it and how long it is kept.
The source of truth is the `[Privacy]` attribute on each type; a test fails if a type is missing
from this page or lacks the attribute. Identity framework tables (roles, claims, logins, tokens,
passkeys) belong to the account and are deleted with it.

## Classifications

| Classification | Meaning |
|---|---|
| Internal | Operational metadata: ids, timestamps, states. No personal content. |
| Personal | Identifies or describes a person, or records their decisions. |
| PrivateSensitive | Written by one user for themselves. Only that user can read it. |
| SharedSensitive | Text its author explicitly approved for one recipient. |

No classification of content (PrivateSensitive or SharedSensitive) may appear in logs,
telemetry, notifications, search indexes or analytics. Logs carry ids only.

## Entities

| Type | Table | Classification | Who can read | Retention |
|---|---|---|---|---|
| `ApplicationUser` | `AspNetUsers` | Personal | The user | Until the user deletes their account; deletion erases all their private data first. |
| `RelationshipWorkspace` | `workspaces` | Personal | Active members | While it has members; purged 30 days after the last member leaves. |
| `WorkspaceMembership` | `workspace_memberships` | Personal | Members of that workspace (own row and partner's joined date only) | With the workspace; deleted with the member's account. |
| `Invitation` | `invitations` | Internal | Active members (status only); holder of the token (workspace name only) | Token hash only. Purged 30 days after it expires, is used or is revoked. |
| `PrivateReflection` | `private_reflections` | PrivateSensitive | Owner only (including notes on a received message; the sender is never told they exist) | Until the owner deletes it or their account. A link to a deleted message is cleared, the notes stay. |
| `ConcernCategory` | `concern_categories` | Internal | Everyone (topic names and descriptions) | Reference data seeded by migrations. |
| `IntakeProfile` | `intake_profiles` | Personal | Owner only (not even whether it exists or is complete) | Until the owner starts over or deletes their account. |
| `ConcernSelection` | `concern_selections` | PrivateSensitive | Owner only | Until the owner removes the topic, starts over or deletes their account. |
| `ExerciseDefinition` | `exercise_definitions` | Internal | Everyone (exercise content); admins (review metadata) | Kept while any attempt uses it. Content is immutable per version. |
| `ExerciseAttempt` | `exercise_attempts` | Personal | Owner only | Until the owner deletes it or their account. |
| `ExerciseResponse` | `exercise_responses` | PrivateSensitive | Owner only | With its attempt; erased with the attempt or the account. |
| `DraftMessage` | `draft_messages` | PrivateSensitive | Owner only | While in use; discarded drafts purged after 30 days; deleted with the account. |
| `SharedMessage` | `shared_messages` | SharedSensitive | Sender always; recipient only while both are active members and it is not revoked | Text erased on revoke; deleted when either party deletes their account or the workspace is purged. |
| `ConsentRecord` | `consent_records` | Personal | The user who gave consent | Life of the account, as evidence; deleted with the account. Stores a SHA-256 fingerprint of approved text, never the text. |
| `AuditEvent` | `audit_events` | Internal | Operators (no UI yet) | 400 days. Ids only; ids of deleted users remain as unlinkable random values. |
| `CoachingUsage` | `coaching_usage` | Personal | The user (in their export); operators see totals only | Daily request and token counts, no text. Purged after 90 days; deleted with the account. |
| `PacePreferences` | `pace_preferences` | Personal | The user; the partner sees only whether a pause is active and when it ends | Until changed; deleted with the account. |
| `Notification` | `notifications` | Personal | The recipient | Kind and message id only, never text. Removed when the message is withdrawn or deleted; purged after 90 days; deleted with the account. |
| `PersonalSafety` | `personal_safety` | PrivateSensitive | Owner only (the partner can't tell whether it exists) | Until the owner deletes their answers or account. Never logged, audited, sent to the coach or used in analytics. |
| `DataDeletionRequest` | `data_deletion_requests` | Internal | Operators (no UI yet) | 400 days after completion. |

## AI coach

Drafts and partner messages sent to the coach are processed in memory and never stored by the
coach, logged or put in telemetry; only daily counts (`CoachingUsage`) are kept. Nothing is sent
to a live provider unless `Coaching:Provider` is `Gateway` and the owner has set
`Coaching:ApprovedForUserContent` after agreeing data processing terms (ADR-0010).

## Access rules (enforced in services, not the UI)

- No read receipts or typing indicators. `SharedMessage.RecipientOpenedAt` exists only for the
  recipient's own "New" marker; no sender-facing view, export of the sender, or notification
  exposes it.
- The inbox, notes on a message and replies are available only to the message's recipient while
  the message is readable (see below). The sender and outsiders get "not found".

- Reflections, drafts, intake and exercise attempts are queried only with `OwnerUserId == acting user`. Another user's id
  returns the same "not found" as an id that does not exist.
- Intake has no workspace scope: no workspace view, partner page or notification reads it, so a
  partner cannot learn which topics were chosen, whether intake was completed or skipped, or any notes.
- Workspace data requires an active membership (`LeftAt IS NULL`) in the query.
- A shared message is created only by `IMessagingService.ShareAsync`, which requires the
  fingerprint of the exact text the author previewed, records a `ConsentRecord` and an
  `AuditEvent`, and can happen once per draft.
- A recipient can read a shared message only while both partners are active members and the
  sender has not revoked it. Leaving the workspace ends access for both directions.
- Personal data export contains the requester's own rows plus messages currently visible to
  them as a recipient. It never includes a partner's reflections, drafts or consent records,
  and it excludes the authenticator key.

## Deletion workflow

1. The user confirms on **Account › Personal data › Delete** (password required).
2. A `DataDeletionRequest` is recorded and processed immediately: reflections, intake topics and
   profile, exercise answers and attempts, shared messages
   sent or received, drafts, consent records, invitations they issued, memberships, then the
   account, in one transaction.
3. If processing fails, the worker retries pending requests every 5 minutes.
4. The partner keeps their account, their private data and the workspace.

## Retention job

The worker applies the retention rules above every 6 hours (`IRetentionService`). Periods are
configurable under `Retention` in configuration.

## Not yet covered

- Field-level encryption and backup retention (milestone 10).
