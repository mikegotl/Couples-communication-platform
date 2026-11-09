# Incident response plan

Status: plan for the private pilot (up to 60 people). Roles are placeholders until the owner
names people; an incident cannot be run without at least the first two.

## Roles

| Role | Who | Does |
|---|---|---|
| Incident lead | _Owner to name_ | Declares the incident, decides, keeps the timeline |
| Operator | _Owner to name_ (one of the two people with Azure access) | Flags, rollback, restores, logs |
| Privacy contact | _Owner to name_ (the data protection contact in the privacy notice) | Decides on regulator and participant notification |
| Clinical contact | _Owner's clinical reviewer_ | Safety content and any harm to a participant |

## Severity

| Level | Examples | Respond within |
|---|---|---|
| **SEV1** | Private content visible to the wrong person (a reflection, draft, intake answer or safety answer shown to a partner or anyone else); a message delivered without its sender's consent; credentials or the database exposed; a safety pathway failing for someone who needs it | Start at once, any hour |
| **SEV2** | App down or sign-in broken for everyone; deletion or export failing; backups failing; a partner learning that a safety check exists or what it said | Same working day |
| **SEV3** | One feature broken with a workaround; elevated errors; a dependency vulnerability with no known exploit path | Next working day |

When unsure, pick the higher level.

## First 30 minutes (SEV1 and SEV2)

1. **Contain before diagnosing.** Use the kill switches, which need only a configuration change
   and restart (Container App revision), no deploy:
   - Everything: there is no per-feature switch for messaging. To stop new people joining, set
     `FeatureManagement__PilotEnrollmentRequired=true` and change `Pilot__AccessCodeSha256` to a
     new value. Enrolled people keep access, so for a SEV1 data exposure **scale the web app to
     zero** instead (`az containerapp update --min-replicas 0 --max-replicas 0`), which takes
     the app offline for everyone.
   - AI coach: `Coaching__Provider=Disabled` (the default until a provider is approved).
   - Feedback form: `FeatureManagement__FeedbackForm=false`.
   - Joint chat (milestone 12): `FeatureManagement__Phase2JointChat=false`; it is off by default.
2. Write the timeline in a private incident note (not a public tracker). Record times in UTC.
3. Preserve evidence: export the relevant Log Analytics query results and the audit events
   (`couples.audit_events`, ids and actions only). Do not copy private content out of the
   database to investigate; query counts and ids.
4. If a deploy is the likely cause, roll back (`docs/operations/rollback-plan.md`).

## Personal data breaches (UK GDPR)

A breach is any accidental or unlawful exposure, loss or change of personal data. Much of this
app's data is special category (health, sex life, relationship conflict, safety concerns).

1. The privacy contact assesses risk to the people affected. Record the decision and reasons
   even when the answer is "not reportable".
2. If reportable, notify the ICO **within 72 hours of becoming aware**, even with incomplete
   facts (they can be added later).
3. If the risk to people is high, tell the affected participants directly and promptly, in plain
   language: what happened, what data, what we did, what they can do. **Consider safety first:**
   an email or notification about a relationship app can itself put someone at risk if a partner
   reads it. Use the channel each person chose, keep the subject line neutral, and never mention
   the content of anything they wrote.
4. A participant affected by a safety-related incident is also offered the support resources on
   `/support`. The app never makes emergency claims; people are pointed to real services.

## After the incident

Within five working days, a blameless review: timeline, what let it happen, what detected it,
what slowed the response, and actions with owners. Add a regression test for the cause (as the
deletion sweep and the form-markup tests were added). Update this plan if a step didn't work.

## Contacts to fill in before the pilot

- Azure support plan and subscription id: _owner_
- ICO breach reporting: https://ico.org.uk/for-organisations/report-a-breach/
- Email provider abuse/security contact: _once chosen_
- AI provider security contact: _once chosen_
