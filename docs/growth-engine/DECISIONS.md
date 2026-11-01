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
