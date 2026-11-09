# ADR-0006: Identity and workspace membership

- Status: Accepted
- Date: 2026-11-09

## Context
Two partners must each have their own account and be able to join one shared workspace, without
either account gaining access to the other's private data, and without invite links becoming a
way in for anyone else.

## Decision
- **Accounts**: ASP.NET Core Identity (Blazor template UI) with `Guid` keys stored in the
  `couples` schema. Confirmed email is required to sign in; lockout after 5 failures for 15
  minutes; minimum password length 12 with no composition rules (NIST SP 800-63B); MFA
  (authenticator app, recovery codes) and passkeys are available. Identity store options that
  shape the database are set once in `AddInfrastructure` so every host builds the same model.
- **Email**: no provider is chosen yet. Development writes emails to a git-ignored folder;
  other environments send nothing and log a warning (no addresses or links). The template's
  on-screen confirmation link was removed because it lets anyone confirm any address.
- **Workspaces**: Phase 1 allows one active workspace per user (partial unique index) and two
  active members per workspace. Each member's id is checked inside `IWorkspaceService`; a
  workspace the caller is not an active member of is reported as not found.
- **Invitations**: 256-bit random tokens, shown to the inviter once; only the SHA-256 hash is
  stored. Single use, 72-hour default lifetime (`Workspace:InvitationLifetime`), at most one
  pending invitation per workspace, revoked when the inviter leaves. Concurrent acceptance is
  resolved with PostgreSQL `xmin` optimistic concurrency, so exactly one person can join.
- **Leaving**: sets `LeftAt` on the membership; access ends immediately. History is kept for
  the retention workflow in milestone 03.

## Consequences
Invite links are bearer secrets: anyone who has an unused, unexpired link and an account can
join. The UI says so and the inviter can revoke or replace the link. Binding an invitation to
the invitee's email is a possible later hardening. Choosing an email provider is required before
any non-development deployment.
