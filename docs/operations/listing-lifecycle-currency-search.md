# Listing lifecycle, currency-aware search and search text (QA Phase 7)

Decisions of 2026-10-14 (P7-D01, D04, D08 and the smaller defects D02, D03, D05, D07).

## 1. Who sees a listing

One rule, written once in `Property` (`OnMarket`, `PubliclyViewable`, `ClosedDealOf`) and used by search,
lists, map/geo search, saved-search matching, comparison, social distribution, analytics and the public detail page.

| Status | Search, lists, map | Public detail by id | Agency page |
|---|---|---|---|
| Available | yes | yes | "Available" tab |
| Reserved | yes (client shows a tag and a lighter card) | yes | "Available" tab |
| Archived | no | 404 (owner and admin still read it through `/manage`) | no |
| Sold / Rented | no | yes, with a status tag | own tab (`agencyId` + `status=Sold|Rented`) |
| Expired | no | no | no |

* Reserved stays listed until it expires, the owner moves it to Sold/Rented, or puts it back to Available.
* `ListingExpiryHostedService` never expires, unpublishes or clears the photos of Sold/Rented listings: they are the
  office's record. Sold/Rented listings do not count against the plan quota.
* `Property.ChangeStatus` stamps `ClosedAt` when a listing becomes Sold/Rented and clears it when it is reopened.
* Owners change the status with `PUT /api/properties/{id}` and `{ "status": "Reserved" }` (only the fields present are applied).

### Photo compression for closed deals

Phase 5 of `ListingExpiryHostedService` (advisory-lock key unchanged, still 8 keys) compresses the photos of listings
closed for 14 days (`CompactionDelayAfterClose`, the buyer's window to back out), at most 20 listings per run, once
per listing (`ImagesCompactedAt`). The implementation (`CloudinaryListingImageCompactor`) re-uploads each photo under
the same public id with an incoming transformation (longest side limited to 1000 px, quality 40), so URLs keep working after
Cloudinary issues a new version. A failed listing moves to the back of the queue and is retried on a later run.
Without Cloudinary credentials the no-op implementation is registered and nothing is touched.

**Not testable from the build sandbox** (no Cloudinary access): test on Staging before relying on it. Mark a test
listing Sold, set `ClosedAt` and `UpdatedAt` back 15 days in the Staging database, wait for the next sweep, and compare
the photo sizes and `ImagesCompactedAt`.

## 2. Currency-aware search

* `GET /api/lookups/currencies` (public, cached 5 min): active currencies, base (USD) first, with `exchangeRateToUsd`
  (units of the currency per 1 USD), `rateUpdatedAt`.
* `PUT /api/admin/currencies/{code}` (admin): creates a currency or sets its rate. Body:
  `{ "nameEn", "nameAr", "symbol", "decimalPlaces", "exchangeRateToUsd", "isActive" }`. Audited as `CurrencyRateUpdatedByAdmin`.
* `minPrice/maxPrice` + `currencyCode`: the range is written in that currency and matches a listing whose price, converted
  to it, falls inside, whatever currency the listing was advertised in. Without `currencyCode` the old raw comparison remains.
* `sortBy=Price` orders by the effective price (sale price for sales, warm then cold rent for rents) converted to USD,
  listings without a price last, then `Id`. It requires `listingType=ForSale|ForRent` (422 otherwise). `PurchasePrice`/`ColdRent` still work.
* Geo search and saved-search matching use the same conversion (one rate lookup per run).
* **Only three currencies are searchable: USD, EUR and SYP** (`SearchCurrencies`). The public list returns only
  these, `currencyCode` with any other code is refused with 422, and only these take part in conversion. A listing
  advertised in another currency (TRY, AED, SAR, ...) matches only searches in its own currency, is not converted and sorts
  last under `sortBy=Price`.
* **Listings are priced in the same three currencies.** `POST/PUT /api/properties` and short-stay listings accept only USD, EUR
  or SYP (one list: `MarketCurrencies` in the domain). Listings created earlier in another currency stay as they are and can
  still change status, but editing their currency requires one of the three, and the price must be entered again in it (the
  amount is never converted for the owner). A body with no `currencyCode` (for example a status change) is not affected.
  Display-currency preferences and the crowdfunding project currency are separate settings and were not changed.
* **The rates are fixed, not live.** Migration `PinSearchCurrencyRates` set them to the values of 2026-10-14: EUR 0.8922
  per dollar (1 EUR = 1.1208 USD). It runs once, so a rate set afterwards through the admin endpoint is kept. To change them
  until a pricing feed exists, use `PUT /api/admin/currencies/{code}`.
* **Syrian pound scale (decided 2026-10-15): the redenominated pound**, two zeros removed on 1 January 2026, about 134 per
  dollar (the old-scale market quote was 13,350 to 13,425). `PinSearchCurrencyRates` first pinned 13,400; migration
  `SetSypRateToRedenominatedScale` corrects it to 134, only while the stored rate is still above 1,000, so a value already set on
  the new scale is kept. Listing prices in SYP must be entered on the same scale, otherwise every conversion is off by 100.

## 3. Search text

`Properties.SearchText` is a stored generated column (title, description, city, region, district, neighborhood) folded by
`SearchTextNormalizer`: Arabic vowel marks and tatweel removed, alef/hamza forms to alef, alef maqsura to yeh, teh marbuta to
heh, Arabic and Persian digits to ASCII. The search splits the typed text into up to 8 words that must all appear (`ILIKE`
with `%`, `_` and `\` escaped, backed by a `pg_trgm` GIN index). `SearchTextNormalizerTests` asserts that PostgreSQL and C# agree.

Adding the generated column rewrites the `Properties` table once when the migration runs (a few seconds at the current size).

## 4. Input limits (public list and geo search)

422 instead of 500 for: NUL characters in `SearchTerm`/`City`/`Region`, a page beyond row 10 000 (`Page x PageSize`),
reversed ranges (price, rooms, area), `sortBy=Price` without a listing type, missing or out-of-range geo coordinates.
Geo search accepts `ForRentAndSale` listings under both listing types, like the list.

## 5. Logging of visitor coordinates (P7-D04)

`appsettings.json` now sets `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics` to `Warning`, which removes the
"Request starting ... ?Latitude=..&Longitude=.." lines. An integration test captures the log and fails if a coordinate appears.
Belt and braces on Render: the environment variable `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Warning`.

## 6. Listing lifecycle rules (QA Phase 8)

| Rule | Where |
|---|---|
| **Input safety (P8-D01).** NUL characters in listing text, messages and visit requests, money above 10¹² and area above 10⁸, an unknown `PropertyTypeId`, and an unknown governorate/district/neighborhood (or ids that do not belong together) return **422** instead of 500. A safety net in `ExceptionHandlingMiddleware` maps PostgreSQL `22021` (invalid byte sequence) and `22003` (numeric out of range) to 422 for paths nobody validated; `23503` and others stay 500 on purpose. | `ValidationRuleExtensions`, `LocationHierarchyChecker`, middleware |
| **Extension (P8-D02/D04/D05).** `Property.CanBeExtended` is false for Sold, Rented, Archived and taken-down listings: quote and confirm return **409** (the fee stays Pending) and the admin extension is refused too. The new window starts at `max(now, ExpiresAt)` for a live listing and at `now` for an Expired one. Only an Expired listing is revived to Available; Reserved stays Reserved; an owner-unpublished listing stays unpublished. Reviving an Expired listing re-checks the plan quota (`IListingQuotaGuard`) at quote time and again inside the confirm transaction under the advisory lock. Expired listings in the grace period still do **not** count toward the quota (deliberate: they must not hold a seat). | `Property.ExtendPublication`, `ListingQuotaGuard` |
| **Publish (P8-D03).** `Publish` refuses an Expired listing (409, "use the paid extension") and a taken-down one. The check runs before the image requirement. | `Property.EnsureCanBePublished` |
| **Storage outage (P8-D06b).** A Cloudinary network failure/timeout returns a failed upload (the existing 502-style message) instead of 500; cleanup of already-uploaded images is best-effort. | `CloudinaryMediaStorageService` |
| **Message limits (P8-D07).** Per (sender, listing): 10 messages per 10 minutes and 100 per 24 hours, otherwise **429** with `Retry-After` and `code` `MESSAGE_RATE_LIMIT` / `MESSAGE_DAILY_LIMIT`. While the receiver still has an unread message from that sender about the listing within 10 minutes, no further notification is created. | `MessagingLimits`, `SendMessageCommandHandler` |
| **Deleting a listing (P8-D08).** The owner's delete and the sweep's post-grace delete cancel Pending, Confirmed and RescheduleProposed visit requests and notify the visitors (best-effort, after the save). | `ListingVisitCleanup` |
| **Administrative take-down (P8-D10).** `POST /api/Admin/properties/{id}/take-down` `{reason}` (required, ≤ 500) and `/restore` `{reason?}`. Take-down unpublishes and un-features, stores `TakenDownAt/By/Reason`, writes an audit row, and notifies the owner (`NotificationType` 44 `ListingTakenDown`, 45 `ListingRestored`; 46 `VisitCancelledByHost`). The owner sees `isTakenDown` and `takenDownReason` on `PropertyDto`; the public sees 404; the sweep neither warns nor expires it. Restore re-publishes it; the public still sees it only while its publication window is open, like any listing. Social posts already published are not retracted (no event exists for it). | `AdminListingService`, migration `AddListingTakeDown` |

**Deploy order.** Merge, run the Migrator (`AddListingTakeDown`: three nullable columns and a filtered index, additive, no table rewrite), then deploy the API, then the app. The app tolerates an API without the new fields.

**Not done on purpose.** P8-D09 (odd but harmless values such as year 1900 or price 0.001) and P8-D06a (no public listing without a photo): see the QA report; the latter changes what the public sees and affects social-distribution timing, so it needs its own decision.
