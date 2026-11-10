# Setting up local date spots (Google Places)

Optional. Until a key is set, "Date ideas near you" shows only the ideas added at
`/admin/rewards`. With a key, it also shows real places in each couple's town, linking to Google
Maps (ADR-0019). The key never goes in the repository.

## Steps (about 10 minutes)

1. Open <https://console.cloud.google.com/> and pick your project (the one used for Google
   sign-in is fine) or create one.
2. Attach a **billing account** to the project. Google requires one even for free usage.
3. In **APIs & Services, Library**, enable **Places API (New)**.
4. In **APIs & Services, Credentials**, **Create credentials, API key**. Then edit the key:
   - **API restrictions**: restrict to **Places API (New)** only.
   - **Application restrictions**: when hosted, restrict to the server's outbound IP addresses.
     (The key is used by the server only; it is never sent to browsers.)
5. In **APIs & Services, Places API (New), Quotas**, lower the per-day request quota to a number
   you are comfortable paying for. In **Billing, Budgets & alerts**, add a budget alert.
6. Give the app the key. Locally, from `src/Couples.Web`:

   ```bash
   dotnet user-secrets set "Rewards:GooglePlaces:ApiKey" "<key>"
   ```

   Hosted: store it in Key Vault (or a Container App secret) as `Rewards__GooglePlaces__ApiKey`.
7. Restart the app, sign in as a paired couple with a town set on **Progress and rewards**, and
   real places appear under "Date ideas near you".

## Settings (`Rewards:GooglePlaces`)

| Setting | Default | Meaning |
| --- | --- | --- |
| `ApiKey` | none | No key, no lookups. |
| `Query` | `date night spots` | Search phrase; the town is added after "in". |
| `DailySearchCap` | 200 | Billable searches per day per app instance. |
| `CacheMinutes` | 30 | Reuse one town's results from memory; 0 asks every time. |

## Cost

Each new town searched is one Text Search request, billed by Google at its "Pro" tier because
only basic fields are requested. Google gives a monthly free allowance per tier and charges per
thousand requests after that; check the current numbers at
<https://developers.google.com/maps/billing-and-pricing/pricing> before going live.

## Before real couples see it

- Read the Google Maps Platform terms, in particular the rules on caching and attribution.
- Add to the privacy notice that the town a couple enters is sent to Google to find places.
- Remember these are suggestions only. A place appearing here has not agreed to anything; offers
  couples can redeem still come from approved partners.

To turn it off, remove the key and restart.
