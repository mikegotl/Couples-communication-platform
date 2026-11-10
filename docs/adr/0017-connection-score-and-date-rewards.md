# ADR-0017: Shared connection score and date rewards

- Status: Accepted
- Date: 2026-11-10

## Context
The owner asked that couples whose communication improves (fewer long breaks, more consistent
positive talk) see that on their dashboard, earn credits from it, and be prompted to cash the
credits in for free dates: restaurant coupons, gift certificates and similar local rewards.

Two things pull against a naive design. The product is privacy-first (private drafts,
reflections, topics and a private safety check-in), and a score that rewards volume would put
pressure on a partner who needs space, or reward the more controlling partner in an unhealthy
relationship. Real local partners and gift-card APIs also need business agreements the owner
doesn't have yet.

## Decision
- **One shared score, never per person.** "Connection rhythm" (0 to 100) is computed per week
  (Monday to Sunday, UTC) from **shared messages only**, between the two current partners, with
  revoked messages excluded. Drafts, reflections, intake, exercises, coach use and safety answers
  never count. Both partners see the same figures; nothing says who did what.
- **Hard to game with volume** (`ConnectionScore`):
  - *Staying in touch*, up to 40: days in the week with a message, counting at most three.
  - *Both voices*, up to 20: a reply, or a message from each partner, at least once that week.
  - *Kind words*, up to 40: the **share** of the week's messages with warm or appreciative wording
    (`WarmLanguage`: a short, readable list of phrases such as "thank you", "I appreciate",
    "sorry", "I hear you", with simple negation). Message text is read in memory only; neither the
    text nor any per-message result is stored, logged or shown.
  The dashboard shows the average of the last four finished weeks, the change from the four
  before, this week so far, the last eight weeks, and the longest quiet stretch in the last four
  weeks, with kind wording and one gentle tip. Tips never ask for more messages; every view says
  breaks are healthy.
- **Credits** are earned per finished week: one per 10 points, plus 2 when the score grows by 10
  or more (and is at least 40). Weeks are scored lazily when either partner opens the dashboard,
  once per workspace (a filtered unique index on `credit_entries`); a new account looks back four
  finished weeks so a couple starts with something. Credits never go down except when spent.
  They belong to the current pair: if the pair changes, the balance starts again at zero and old
  weeks are never re-scored.
- **Cashing in**: either partner can redeem; both see the voucher. The balance is reserved first
  (optimistic concurrency on `reward_accounts.xmin`), then the provider issues the voucher with
  the redemption id as the idempotency key; if issuing fails the credits are refunded. Each
  redemption is audited (ids only).
- **Provider interface** (`IRewardCatalogProvider`). The built-in `Local` provider uses the
  admin-managed catalog (`/admin/rewards`) and issues `BT-` codes a participating business checks
  with the pilot team; sample offers issue `SAMPLE-` codes that say they can't be used. Offers are
  either available anywhere or tied to a town or area the couple can type.
- **Feature flag `DateRewards`**: on in Development with a seeded sample catalog
  (`Rewards:SeedSampleCatalog`), off in Production until a real business or provider is signed
  up. With the flag off couples still see the score; no credits are earned and redeem endpoints
  refuse.
- **Safety**: someone whose own safety check or safety mode holds joint features sees no score
  at all (only they see that view). For the partner, rewards disappear exactly as if the flag were
  off (no credits earned, offers hidden, redeem refuses), with no reason given. No date
  suggestions are made to a couple while either partner may be unsafe.

## Plugging in a real provider
Implement `IRewardCatalogProvider` (for example over a gift-card API such as Tremendous, Tango
Card or Giftbit, or a local-offers platform) and register it for `Rewards:Provider`. It must:
- treat the redemption id as an idempotency key, so a retry never issues or charges twice;
- receive nothing about the couple except that id (no names, emails or area beyond what the
  offer itself needs);
- keep API keys in configuration or Key Vault, never in the repository;
- map its catalog to `RewardOffer` (title, business, area, cost in credits, kind).
A paid provider also needs a budget cap, reconciliation of issued vouchers against invoices, and
the owner's decision on what a credit is worth.

## Consequences
- The partner of someone with a safety hold can notice that rewards they saw before have gone.
  This matches joint chat (ADR-0012, ADR-0015): features stop without a reason, and the same view
  appears when rewards are simply switched off.
- Past pauses aren't recorded, so a week spent paused scores low. It only earns fewer credits and
  never costs any; recording pause history to exclude those days is a possible follow-up.
- The warm-words list is English-only and deliberately conservative; it can be reviewed with the
  clinicians who review exercises.

## Not decided (owner)
- Real reward partners or a gift-card provider, the value of a credit, and a monthly budget.
- Whether rewards should be part of the pilot at all (the flag is off in Production).
