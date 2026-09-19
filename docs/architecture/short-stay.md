# Short-Stay listings — domain, quota, types, photos, currency

Verified 2026-09-19 against `master` + branch `fix/short-stay-endpoints-currency`.
Frontend counterpart: `src/app/features/short-stay/*` in the Angular repo.

## Domain

`ShortStayListing` is a standalone aggregate (optional loose link to a `Property` via `PropertyId`).
Creating one also creates a default `RoomType` + `AccommodationUnit` so it is bookable immediately.
**Every price is per night** (`RoomType.BasePricePerNight`, `PricingRule.PricePerNight`); fees
(`CleaningFee`, `ExtraGuestFee`, `ExtraBedFee`) are flat per booking. There is no other pricing unit.

## Plan quota

- A Short-Stay listing consumes the **same** pool as Sale/Rent. `IActiveListingCounter`
  (`Infrastructure/Listings/ActiveListingCounter.cs`) sums Property + Short-Stay; agencies pool
  through their members (`ShortStayListing` has no `AgencyId`).
- `CreateShortStayListingCommandHandler` mirrors `CreatePropertyCommandHandler`: contact-confirmed
  gate, plan-selected gate, then inside one transaction an advisory lock
  (`ListingQuotaLock.ForOwner/ForAgency`, shared with Property) → count → limit check → insert →
  commit. Over the limit → 409 `ConflictException`. The limit comes from `IListingQuotaPolicy`
  (free and paid plans alike). Enforcement is server-side only; the frontend just surfaces the 409.
- Counted: every non-deleted listing, published or not. Soft-deleted listings free the slot.
- Not counted yet: nothing expires (no `Expired` state for Short-Stay).

## Accommodation Type

- A **database lookup** (`AccommodationTypes`, seeded idempotently by `AccommodationTypesSeed`:
  16 codes in 4 categories; admin-extensible without a deploy). Not an enum.
- Purpose: classifies the listing (furnished apartment, villa, chalet, hotel room, camp …). It is
  required at creation (`AccommodationTypeId` FK, `Restrict`), returned in DTOs as
  `accommodationTypeCode` / `…NameAr`, and used as the `accommodationTypeId` **search filter**.
  It does not affect pricing, capacity or amenities.
- Source for the dropdown: `GET /api/short-stay/listings/accommodation-types` (anonymous, rate
  limited `shortstay-search`, active types ordered by `SortOrder`).
- Validation: `CreateShortStayListingCommandHandler` → 404 if the id is unknown; the validator
  rejects `<= 0`. The type is fixed after creation (not part of the PUT contract).

## Photos

`POST /api/short-stay/listings/{id}/photos` (multipart field `files`, authenticated). Reuses
`IMediaStorageService` (Cloudinary) exactly like Property images. Rules in
`AddShortStayListingPhotosCommandHandler`: owner only (403), max 10 files/request, 20 photos per
listing, 5 MB each, MIME jpeg/png/webp, extension allowlist, magic-byte signature check, uploads
rolled back in storage if the DB save fails. First photo is the main photo. There is currently no
photo delete/reorder endpoint.

## Currency

- `ShortStayListing.CurrencyCode` (`varchar(3)`, NOT NULL, default `SYP`) — the same ISO codes and
  default `Property.CurrencyCode` uses. Allowlist in `ShortStayListing.SupportedCurrencyCodes`
  (kept identical to `CreatePropertyCommandValidator`); the frontend form offers SYP/USD/EUR like
  the property form. No conversion anywhere.
- **Set at creation, immutable.** Bookings snapshot amounts without their own currency, so a change
  would silently re-denominate them.
- Returned in `ShortStayListingDto`, `ShortStayListingSummaryDto` and `BookingDto`.

## API changes (this branch)

| Endpoint / DTO | Change |
|---|---|
| `GET /accommodation-types` | new (cherry-picked from `feat/short-stay-accommodation-backend`, never merged before) |
| `POST /{id}/photos` | new (same) |
| `PUT /{id}/amenities` | new (same) — `[Guid]` body, owner only |
| `GET /bookings/pricing-preview` | new (same) |
| `POST /` request | + required `currencyCode` |
| `ShortStayListingDto`, `ShortStayListingSummaryDto`, `BookingDto` | + `currencyCode` |

## Database

Migration `AddShortStayListingCurrencyCode` — one additive column. Existing rows receive `SYP`
(the system-wide default, i.e. what every price was implicitly quoted in before); no data rewrite.

## Tests

`ShortStayListingTests` (currency default / normalisation / rejection), `ShortStayHardeningValidatorTests`
(currency validator), `CreateShortStayListingCommandHandlerTests` (quota gates),
`AddShortStayListingPhotosCommandHandlerTests`, `SetShortStayListingAmenitiesCommandHandlerTests`,
`GetActiveAccommodationTypesQueryHandlerTests`, `PublicEndpointPolicyTests`.
