# ADR-0019: Local date spots from Google Places

Status: accepted (2026-11-10). Off until a key is configured.

## Context

"Date ideas near you" (ADR-0018) only showed ideas an admin typed in. The owner wants couples to
see real places in their own town without the list being curated by hand for every town.

## Decision

- `ILocalPlaceSearch` (Application) with one implementation, `GooglePlacesSearch`, calling
  Places API (New) Text Search. The date idea provider merges results: curated ideas for the
  town, then Google places, then curated ideas that work anywhere.
- **Off by default.** Without `Rewards:GooglePlaces:ApiKey` no request is ever made. The key lives
  in user secrets or Key Vault, never in the repository.
- **Only the town leaves the app.** The request is the search phrase plus the town the couple
  typed. No user, workspace, score, message or location coordinates. The call is made by the
  server, so Google never sees the couple's browser or IP address. Logs carry a status code only,
  never the town or the key.
- **Suggestions, not rewards.** Places are not partners: no voucher, no credits, no claim that
  the business takes part. Redeemable offers still require an approved partner (ADR-0018).
- **Cost control.** The field mask asks only for name, address, type and Maps link (no ratings,
  photos, reviews or opening hours, which are billed at higher tiers). One search per town is
  reused from memory for `CacheMinutes` (default 30), failures pause for 5 minutes, and
  `DailySearchCap` (default 200 per app instance per UTC day) stops new searches. A quota set in
  Google Cloud is the real hard limit and should be set too.
- **Fail quiet.** Any error, timeout (4 s) or cap shows curated ideas only.
- **Display.** Each place links to its Google Maps page (only https links on Google hosts are
  accepted from the response) and the section credits Google Maps. Nothing from Google is
  written to the database.
- The safety hold is unchanged: a couple whose rewards are hidden sees no ideas and triggers no
  search.

## Consequences

- Results are whatever Google returns for "date night spots in <town>"; they are not vetted. The
  phrase is configurable (`Query`).
- The cap is per instance and resets on restart; with several instances the Google Cloud quota is
  the limit that counts.
- Owner decisions before turning it on for real couples: accept Google Maps Platform terms
  (including their rules on caching and attribution; set `CacheMinutes` to 0 if in doubt), add
  Google as a recipient of the town in the privacy notice, and set a budget alert.
