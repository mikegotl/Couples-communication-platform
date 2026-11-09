# ADR-0007: Consent ledger and per-message sharing

- Status: Accepted
- Date: 2026-11-09

## Context
The product's core promise is that nothing a partner writes reaches the other partner unless
they explicitly approve it. That has to hold in the service and data layers, survive UI bugs, and
be provable later.

## Decision
- **Ownership**: reflections and drafts carry `OwnerUserId` and are only ever queried with the
  acting user's id. Workspace membership grants nothing over them.
- **Sharing** is one operation, `IMessagingService.ShareAsync(user, draft, approvedFingerprint)`.
  The fingerprint is the SHA-256 of the exact text shown in the preview; if the draft changed,
  sharing fails with `ConsentMismatch`. The approved text is copied into a separate
  `SharedMessage` row, so later draft edits cannot alter what was shared.
- **Consent ledger**: each share writes a `ConsentRecord` (user, purpose, subject id, content
  fingerprint, granted/withdrawn time) in the same transaction. The ledger never stores content.
- **Revocation**: the sender can revoke; the recipient loses access and the text is erased from
  storage; the consent record is marked withdrawn.
- **Visibility**: a recipient can read a message only while both partners are active members.
- **Classification**: every entity declares `[Privacy(classification, retention)]`; tests fail if
  one is missing or absent from `docs/privacy/data-inventory.md`.
- **Deletion**: a `DataDeletionRequest` is processed immediately in the web request and retried
  by the worker; it erases content rows explicitly before deleting the account.

## Consequences
Every new feature that stores user text must add an owned entity with `[Privacy]`, go through a
service that filters by owner, and be added to the data inventory. A shared message cannot be
recalled from the recipient's memory, and the UI must say so.
