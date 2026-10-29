# Listing view counter

Counts how many times a listing's **detail page** was opened. Applies to both listing aggregates:
sale/rent (`Property`) and short-stay (`ShortStayListing`).

## Definitions (do not mix them under one name)

| Metric | Meaning | Where it lives | Kept |
|---|---|---|---|
| **Total views** (`viewCount`) | Counted detail-page opens since the counter was introduced. A viewer adds at most one per listing per cooldown window. | `ListingViewCounters.ViewCount` | forever |
| **Views, last 7 / 30 days** | Counted views inside the window. | `ListingViewEvents` | `ListingViews:RetentionDays` (90) |
| **Distinct visitors, 30 days** | Distinct keyed hashes among the counted views of the last 30 days. An *estimate*: it identifies an account or an anonymous browser install, not a person. | `ListingViewEvents` | same |

A card in a list, a search result, a map marker, a `GET` of the listing, an image request or a
failed page load is **not** a view.

## What counts, and what never does

`POST /api/properties/{id}/views` and `POST /api/short-stay/listings/{id}/views` are sent by the
client once the detail page is on screen (and visible). The server decides:

1. Listing missing, deleted, unpublished, expired, archived, taken down -> `404`, nothing stored (same rule as the detail page: `Property.PubliclyViewable`).
2. Test traffic (`X-HN-Test-Traffic` equal to `ListingViews:TestTrafficSecret`; ignored when no secret is configured) -> not counted.
3. Crawlers / headless browsers / HTTP libraries / empty user agent -> not counted (courtesy filter only).
4. The owner, administrators, and members of the agency the listing is published under -> not counted.
5. Otherwise the viewer is identified by, in order: the signed-in account, the opaque `X-Visitor-Id` (random id kept by the web/app install), a coarse server fallback (network /24 or /64 + user agent). IP alone is never the identity when a visitor id exists.
6. One count per (listing, viewer, **cooldown window**). The window number is `floor(unixSeconds / cooldownSeconds)` (`ListingViews:CooldownHours`, default 24, 1-168): fixed windows, so a viewer can be counted again right after a window boundary. This is deliberate: it is what lets the dedupe be a unique key instead of a read-then-write race.

The client never sends a count. The response is `{ counted, viewCount, reason }` where `reason` is
`cooldown | own-listing | automated | test-traffic`.

## Storage

* `ListingViewCounters (Kind, ListingId) -> ViewCount` — its own table, **not** a column on `Properties`/`ShortStayListings`: both carry an `xmin` optimistic-concurrency token, so bumping a counter on the listing row would make owner edits and background sweeps fail with 409 whenever a view lands in between.
* `ListingViewEvents` — the dedupe ledger and history. Unique index `UX_ListingViewEvents_Dedupe (Kind, ListingId, ViewerKeyHash, Bucket)`. `ViewerKeyHash` is an HMAC-SHA256 (key derived from `Security:PhoneLookupHmacKey`, label `hudhudnest:listing-views:v1`). No user id, IP or user agent is stored.
* One SQL statement does `INSERT ... ON CONFLICT DO NOTHING` into the ledger and, only if a row was inserted, the upsert-increment of the counter. Concurrent or retried requests for the same key add exactly one; distinct viewers lose no increments.
* `ListingViewRetentionHostedService` deletes ledger rows older than the retention window daily (advisory lock, batched). Totals are not affected.

The migration `AddListingViewCounter` only creates the two tables; nothing existing is altered, backfilled or reset. Existing listings start at 0.

## Reading

* `viewCount` is added to the property DTOs (list, detail, mine, manage) and the short-stay DTOs (search, detail, mine). The public property detail response is output-cached, so it can lag by the cache TTL; the page raises the number from the answer to its own `POST .../views`.
* `GET /api/listing-views/stats?scope=mine|agency` (authorized): `mine` = the caller's own listings of both kinds; `agency` = listings published under the caller's agency, agency owners only (`403` otherwise). Agency scope covers sale/rent listings only (short-stay listings have no agency link).
* `GET /api/listing-views/{property|shortstay}/{id}/trend?days=` (authorized): daily counts, zero-filled, max 90 days / the retention window. Allowed for the owner, an administrator, or the owner of the listing's agency; `403` for anyone else, `404` for an unknown id.

## Limits (be honest in reports)

* No mechanism detects every fake view. A script that rotates `X-Visitor-Id` is bounded only by the `listing-views` rate limit (30/min per client) and the cap that each id counts once per window. Bots that present a normal browser user agent are not detected.
* Anonymous visitors who clear site data get a new id and can be counted again.
* The fallback identity (no account, no visitor id) under-counts people who share a network and user agent.
* Distinct-visitor numbers are estimates (see above). History older than the retention window is gone; only the total remains.
