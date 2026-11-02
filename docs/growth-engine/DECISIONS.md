# Growth Engine — Decisions

Each entry: decision, reason, who decided. "Default" means Claude chose a reversible default within the brief
and the owner can change it.

## D-01 Extend the existing modular monolith (2026-11-01, brief §2)
No new services, queues or infrastructure. Background work uses the existing `BackgroundService` +
`BackgroundJobLock` (Postgres advisory lock) pattern; notifications go through the existing
`INotificationService` (dedupe keys, preferences, push). Reason: it already meets the needs at current
scale and the owner wants minimum spend.

## D-02 Price history counts only publicly visible changes (default, 2026-11-01)
Price edits are recorded only while the listing is published. Draft edits are not market events and
would show "changes" nobody could have seen. A currency switch is recorded as its own change with the
old currency kept (`OldCurrencyCode`), and percent change is shown only when both values share a
currency (no conversion through fixed rates, which would invent a change).

## D-03 Verification documents need private storage (open, owner decision)
Cloudinary assets used today are public URLs (see QA phase 10, D02). Ownership or ID documents must not
be stored there. Options: Cloudinary authenticated delivery, or a private bucket. Until he decides, P9 is
blocked; no document upload is built.

## D-04 Search requests never reveal the requester (default, 2026-11-01)
Advertisers see anonymous requests (area, type, budget band, rooms, area range, features, free-text
details after a contact-data filter). They can "propose" a listing; the requester receives it as a
notification and decides whether to contact. No phone/email/name is exposed by any request endpoint.

## D-05 Unknown specs are not matches (brief §12)
In matching, a criterion the listing does not state counts as "unknown" (no points, reason listed), never
as a match. Hard rules (listing type, governorate, property type when given, budget ceiling with
tolerance) exclude; soft rules (district/neighborhood, rooms, area, features, price position) score.

## D-06 Price-change alerts go to users who saved the listing (default, 2026-11-01)
At most one notification per listing per sweep per user (the latest change wins), sent by a background
sweep every 10 minutes, deduplicated by history row id. The owner keeps the existing confirmation.

## D-07 Saved-search edits and resume start from "now" (default, 2026-11-01)
Editing a saved search or resuming a paused one moves its cursor to the current time. Listings that were
already on the market are not sent as "new" in one burst. The owner can still run the search by hand to see
them. Location and property type are not editable in place (the app sends them unchanged); a new search is
saved instead, which keeps the edit form small.

## D-08 Daily summary instead of a per-listing push (default, 2026-11-01)
A search set to "daily" gets at most one notification per 24 hours with the number of new listings
(deduplicated per search per UTC day). "Instant" keeps the old behavior: one notification per listing, at most
10 per sweep. No weekly option yet; add one only if users ask.

## D-09 Delivery log as counters, not a table (default, 2026-11-01)
Each saved search keeps `LastNotifiedAt` and `NotifiedCount`. The notifications themselves are the detailed
log (already stored, deduplicated and visible in the inbox). A separate delivery table would duplicate them.

## D-10 One growth event log, written after the action is saved (default, 2026-11-01)
`GrowthEvents` is append-only with stable snake_case names (`GrowthEventNames`), a unique idempotency key
per fact (for example `favorite_added:{user}:{listing}`, so saving, removing and saving again counts once)
and no personal data: internal ids, UTM-style tags (lower case, letters, digits, `-_.` only, others dropped)
and the trace id. The recorder runs after the action is committed and never throws, so analytics cannot fail
or roll back a user action. Acquisition uses the first UTM touch within 30 days (the app's existing
attribution store). Events written now: user_registered, listing_published (first time only),
search_request_created, saved_search_created, favorite_added, contact_started, listing_proposed.
Google/Apple sign-ups are not yet recorded as user_registered (the dashboard counts accounts from
UserAccounts instead); to add when the social login is touched next.

## D-11 Referrals qualify on real activity; rewards stay manual until decided (default, 2026-11-01)
An invitation counts only when the invited account (a) still exists and is not banned, (b) confirmed a phone
number, and (c) has a listing that has stayed on the market at least 7 days. Self-referral is impossible
(new account), one referrer per account, a 90-day window, and at most 10 counted invitations per referrer per
30 days (above that: rejected `referrer_cap`). All values live in `Referrals` settings. **No reward is granted
automatically**: an admin records "reward given" on a counted invitation (audited). Reason: any automatic
reward is a commercial promise and an incentive to game; the owner decides the reward (free featured days,
plan month, cash) and its text (`Referrals:RewardDescriptionAr/En/De`, shown on the invite page). Until then
the invite page says no reward is announced. The referrer never sees who signed up, only dates and statuses.

## D-12 Price estimates are asking-price statistics, never a valuation (default, 2026-11-01)
Method in `PRICE-METHODOLOGY.md`. Defaults: at least 8 listings from at least 3 owners after removing
duplicates and Tukey outliers; range = P25..P75 per m² × area; confidence by sample size, lowered for a wide
spread; rebuilt every 6 hours by a background job; public and anonymous because it holds no personal data.
The old `GET /api/analytics/market` (G15.1: averages across mixed currencies, no minimum sample, 0 for "no
data") is not used by the app and is left unchanged so no outside caller breaks; it is deprecated in favor of
`/api/price-estimates/areas`. Remove it after a release if nothing calls it.

## D-13 Campaigns are measured, not rewarded; no automatic campaign grants (default, 2026-11-01)
P7 delivers campaign *tracking*: UTM links built in the admin page, sign-ups per source/medium/campaign and
what those accounts did in their first 30 days, inside the growth dashboard (P10). It does **not** add an
automatic grant ("first 100 agencies get a free month") on top of `Offer`: like referral rewards (D-11), that
is a commercial promise and needs the owner's decision on what is granted, to whom, and how it is honored
(there is no billing yet; `Offer` is a lead-capture promise handled by hand). The existing admin "Offers" tab
keeps covering limited-seat offers. Retention uses a new `user_active` event per account per day, written by
a background job from refresh tokens, so the sign-in path is unchanged. Definitions: `GROWTH-METRICS.md`.

## D-14 Map: server-side clusters; hiding the exact spot is the owner's opt-in (default, 2026-11-01)
`GET /api/properties/map` returns up to 300 points for a box, or grid clusters (cell = a quarter of a map tile
at that zoom) when there are more. New per-listing choice `HideExactLocation`: when on, every public output
(detail, lists, near-me search, map) shows the centre of a fixed ~550 m grid cell instead of the pin, no street,
and near-me distances rounded to 500 m; the owner and admins still see the exact pin. A fixed cell (not random
jitter) is used so repeated requests reveal nothing more. **Default is off for existing and new listings**, so
nothing changes for anyone who does not tick the box. Recommendation: make it the default for private
(non-agency) owners; that is a product choice for the owner. Short-stay listings are not covered yet.
