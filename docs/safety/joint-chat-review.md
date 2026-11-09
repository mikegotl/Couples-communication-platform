# Joint chat (Phase 2): sign-off checklist

Joint chat is built and tested but **must not be shown to any participant** until two
independent reviews are signed and recorded. Nothing here has been signed. The code enforces
this: the `Phase2JointChat` flag is off by default, and even with the flag on, no workspace can
be made eligible and no chat can start until both of these are set in configuration (never in
the repository):

```
JointChat__ClinicalSignOff=<reference to the signed clinical review>
JointChat__SecuritySignOff=<reference to the signed security review>
```

Then an administrator makes individual, recruited workspaces eligible at `/admin/joint-chat`.

## Clinical review (licensed clinician, independent of the build)

| Item | Where | What to check |
|------|-------|---------------|
| Whether a live, text-based joint conversation is appropriate at all for this pilot, and for whom | ADR-0015 | Inclusion and exclusion criteria for eligible couples; recruitment and screening process behind the pilot reference |
| Rules shown before consent | `Components/Pages/JointChat/Rules.razor` | Plain, non-coercive, honest about limits (not therapy, not monitored live) |
| Consent and declining | `JointChatPage.razor` | Each partner agrees to each chat; declining is easy and needs no reason; the inviter is not told why |
| Turn-taking and timings | `JointChatPolicy` | Turn timeout (5 min), idle end (20 min), total limit (60 min), invitation expiry (30 min), pause lengths (5/10/20 min) |
| Pause and leave | `JointChatSession` | Only the person who paused can end a break; leaving ends the chat for both at once |
| Moderation word list | `JointChatPolicy.HeldWords` | Which words are held for rephrasing, and the rephrase prompt's tone |
| Risk phrases | `Couples.Domain.Safety.RiskSignals` | Shared with the coach. A match ends the chat, the text is not delivered, and only the writer sees support |
| Endings | `JointChatEnding` | The other partner always sees "This chat has ended", so a safety ending can't be told apart |
| Safety gating | ADR-0012, `ISafetyGate` | Joint chat is off for a workspace while either partner's check-in or safety mode holds joint flows |
| Transcript retention | `JointChatPolicy.TranscriptKeptAfterEnd` | Both can read it for 7 days after it ends; is that right, or should it be shorter? |

## Security review (independent of the build)

| Item | Where | What to check |
|------|-------|---------------|
| Flag off means off | `JointChatGate`, `JointChatService`, `JointChatHttpTests` | 404 before authentication; every service entry checks the flag; nothing written |
| Authorization in the service layer | `JointChatService.ContextAsync`, `ParticipantSessionAsync` | Only the two current members of an eligible workspace reach a chat; ids from the page are never trusted |
| Admin actions | `JointChatAdminService` | Admin role checked against the database; eligibility needs both sign-offs; audited |
| Concurrency | `JointChatService.Fresh`, `xmin` row version, the one-open-chat index | Two partners acting at once can't corrupt turn or pause state |
| Data handling | `docs/privacy/data-inventory.md` | Held text never stored; no text in logs, audit or metrics; export, deletion and retention |
| Single replica | `IJointChatNotifier` | Live updates are in-process; the Azure Terraform pins the web app to one replica |
| Abuse | `JointChatPolicy` | One message per turn, 1,000 characters, time limits |

When both are signed, record the references in configuration and in PROGRESS.md, then make
workspaces eligible one at a time.
