# ADR-0018: Reward partners and date ideas

- Status: Accepted
- Date: 2026-11-10

## Context
ADR-0017 added credits and a reward catalog an admin fills in by hand. The owner asked how local
date-night places should get onto the rewards page: show any place nearby, or register places
first, and how to recruit them. He chose both tiers: registered partners for anything redeemable,
plus plain ideas so the page is never empty.

## Decision
- **Only registered partners are redeemable.** A voucher is a promise; a business that never
  agreed to it won't honour it. A business applies on the public page `/partners`
  (`RewardPartner`, status Pending). Nothing is shown to couples until an admin approves it on
  `/admin/partners`, which creates its first `RewardOffer` (never a sample; name, area and kind
  come from the partner) in the same step.
- **Check link.** Approval gives the admin a private link (`/partner/check/{key}`) to send to the
  business. On it staff type a couple's code, see whether it is valid, used or expired, and mark it
  used once. Only the SHA-256 hash of the key is stored, as for invitations; an admin can replace
  the link, and declining or ending the partnership removes it and hides the partner's offers.
  The page shows the offer title and dates only, never who the couple is. A code belonging to
  another partner looks exactly like an unknown code.
- **Abuse limits.** The application form has a honeypot field, a site-wide cap per day
  (`Rewards:PartnerApplicationsPerDay`), and shares the per-address post limit used for sign-in
  forms; so does the check page, which limits code guessing (codes carry 64 random bits and need
  the partner's key as well). A repeat application from the same business while one is pending
  stores nothing new.
- **Date ideas** (`DateIdea`, admin-managed on `/admin/rewards`) appear under the rewards as
  "Date ideas near you": things to do together or local spots, local ones first, then ideas that
  work anywhere. They carry no voucher and say so; links must be https and open the place's own
  site. Six general ideas are seeded with the sample catalog. `IDateIdeaProvider` is where a
  places service (for example Google Places) could plug in; it would be given the town only.
- **Safety and flag**: ideas and partner offers sit inside the rewards view, so they follow the
  `DateRewards` flag and disappear with rewards during a safety hold (ADR-0017).
- **Privacy**: partner contact details are business contacts, visible to admins only, never
  logged; declined or ended partners are purged after 180 days.

## Consequences
- There is no email to applicants yet (no email provider is chosen); the admin contacts them
  using the details on `/admin/partners`.
- The couple's area is a typed town name matched exactly (ignoring case). Postcode or distance
  matching would need a geocoding service.
- Recruiting partners and the written agreement are the owner's work; a starting playbook is in
  `docs/operations/reward-partners.md`.
