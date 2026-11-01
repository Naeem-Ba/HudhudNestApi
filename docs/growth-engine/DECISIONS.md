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
