# Pilot readiness checklist

The private pilot (up to 60 people, invitation code, UK) starts only when every box below is
ticked by the person named. Items marked **owner** cannot be done by engineering.

## Clinical and safety
- [ ] **Owner**: a licensed clinician reviews and signs off every published exercise
      (`/admin/exercises`; `Exercises:RequireClinicianApproval` stays `true`).
- [ ] **Owner**: the clinician reviews the private safety check, its wording, outcomes and the
      silent hold on joint flows (`docs/safety/clinical-review.md`).
- [ ] **Owner**: UK support resources confirmed current (`Safety:Resources`), each with an https
      link, and the `/support` page read end to end.
- [x] The app never claims to be therapy or to contact emergency services (tests and copy
      review, milestones 07 and 09).

## Legal and privacy
- [ ] **Owner**: legal review of `docs/legal/privacy-notice.md` and `docs/legal/pilot-terms.md`.
      Fill in the controller's name and address, the contact email, the data protection contact
      and the processors (hosting, email, AI if used). When the text changes, bump
      `PilotDocuments.TermsVersion` / `PrivacyNoticeVersion` so everyone agrees again.
- [ ] **Owner**: a DPIA (data protection impact assessment) for special category data, using
      `docs/privacy/data-inventory.md` and `docs/security/threat-model.md`.
- [ ] **Owner**: register with the ICO (data protection fee) if not already.
- [ ] **Owner**: data processing agreements with the hosting provider (Azure), the email provider
      and, only if the coach is turned on, the AI provider.

## Hosting and operations
- [ ] **Owner**: approve the Azure staging spend and `terraform apply` (`deploy/azure/README.md`).
- [ ] **Owner**: choose the email provider; engineering wires it in (account confirmation and
      password reset need it).
- [ ] **Owner**: name the incident lead, operator, privacy contact and clinical contact
      (`docs/operations/incident-response.md`).
- [ ] Restore drill: point-in-time restore of staging into a new server, then the deletion
      re-erasure steps (`docs/operations/backup-policy.md`).
- [ ] Rollback drill: deploy version N+1, roll back to N (`docs/operations/rollback-plan.md`).
- [x] Container images build in CI; the staging rehearsal (`deploy/staging`) runs the pilot
      flow over HTTPS with TLS to the database and an encrypted key ring.

## Accessibility
- [x] Automated audit: 0 axe violations on 17 pages (`docs/accessibility/audit-2026-11.md`).
- [ ] A manual screen reader pass (NVDA with Firefox, VoiceOver on iOS) of sign-up, the pilot
      page, writing and sending a message, the safety check and `/support`.
- [ ] 200% zoom and 320px reflow check of the same pages.

## Pilot configuration
- [ ] **Owner**: choose the invitation code and share it only with invited participants. Store
      only its hash:
      ```bash
      printf '%s' 'the-code-you-chose' | sha256sum | cut -d' ' -f1
      ```
      Put the result in `pilot_access_code_sha256` (Terraform) or `Pilot__AccessCodeSha256`.
      To close enrollment, change the hash; enrolled people keep access.
- [ ] `Pilot:MaxParticipants` set (default 60).
- [ ] `FeatureManagement:Phase2JointChat` is `false`.
- [ ] `Coaching:Provider` stays `Disabled` unless the AI provider decision and DPA are done.
- [ ] Admin accounts listed in `Administration:AdminEmails`; `/admin/pilot` shows counts.

## During the pilot
- Read feedback weekly (`/admin/pilot`); participants who allowed contact can be followed up.
- Review the operations overview (counts under 5 are hidden) and the error rate weekly.
- Any report of harm or of private content seen by a partner is a SEV1
  (`docs/operations/incident-response.md`).
