# Phase-0 Implementation Notes (Tasks 0–4, + Angular frontend follow-up)

This document records what was actually implemented in this pass, how to finish
verifying it, and what was deliberately left out. Written in English per request,
even though the rest of the codebase mixes Arabic/English comments — new code in
this pass follows the English-comment convention throughout.

## 0. How this was built — and its most important limitation

All code in this pass was written directly against the real files in this
repository (staged from your machine into a sandboxed cloud environment,
edited there, and committed back). **`dotnet build` / `dotnet test` / `dotnet ef`
could NOT be run in that environment** — its network policy blocks
`api.nuget.org` entirely (a hard proxy-level block, not a transient failure),
so package restore is impossible there, and no local NuGet cache existed to
fall back on. This is disclosed here deliberately: every file below was
produced through careful manual pattern-matching against your existing code and
line-by-line review, not through a compiler feedback loop. That is a materially
weaker guarantee than "builds and tests pass," and you should treat this as a
**reviewed draft, not a verified change**, until you run the commands in
section 6 yourself.

Three concrete mistakes were caught and fixed *during* this review specifically
because of the extra manual pass (listed here so you know what kind of thing to
double check elsewhere too):

1. Two test helper classes (`StubPropertyRepository` in
   `PropertyOwnershipServiceTests.cs`, and a `NoOpUnitOfWork` I wrote for the new
   availability-confirmation test) initially didn't implement new interface
   members / interface methods added elsewhere — a real compile error that only
   surfaced by re-reading every implementer of `IPropertyRepository` /
   `IUnitOfWork` after changing those interfaces.
2. `AppDbContextModelSnapshot.cs` was initially hand-edited to match the *new*
   Transaction column definitions. That's wrong: the snapshot must stay
   representing the *last migrated* state so `dotnet ef migrations add` can diff
   against it and generate real `AlterColumn` statements. It was reverted —
   **do not hand-edit `AppDbContextModelSnapshot.cs`**; let `dotnet ef migrations
   add` regenerate it for you (see section 6).
3. `SavedSearchMatchHostedService` originally called `.Add()` on an
   already-tracked entity after mutating it — that throws at runtime (or is a
   silent no-op) depending on EF's tracking state; fixed by making
   `ISavedSearchRepository.GetAllAsync()` return tracked entities and relying on
   the existing `SaveChangesAsync` to pick up the mutation.

## 1. Task 1 — Transaction stringly-typed fields → enums (tech debt)

- New: `HudhudNestApi.Domain/Transactions/Enums/{TransactionType,TransactionStatus,TransactionPaymentMethod}.cs`
- Changed: `Transaction.cs` (properties are now the enums; factory/domain methods unchanged in shape)
- Changed: `TransactionConfiguration.cs` (`HasConversion<string>()`, `HasMaxLength(30)`, same pattern `PropertyConfiguration` already uses for `Property`'s enums)
- New tests: `tests/HudhudNestApi.Application.Tests/Transactions/TransactionTests.cs`

`Transaction` has no callers anywhere in the codebase today (confirmed via a
full-tree search before touching it), so this is a genuinely zero-risk-to-runtime
change — the only risk is compile-time, covered by the tests above and the
review pass.

**Migration needed** — do not use a hand-written one; see section 6.

## 2. Task 0 — Structured location wiring (tech debt, prerequisite for Task 4)

`Property` already had `GovernorateId` / `DistrictId` / `NeighborhoodId` /
`PropertyTypeId` columns, but nothing in the Application layer ever set or
filtered on them — `CreatePropertyCommand`, `UpdatePropertyCommand`, and
`PropertyFilterDto` only carried the legacy free-text `City`/`Region`.

- Changed: `CreatePropertyCommand.cs` / `CreatePropertyCommandHandler.cs` — accepts and sets the four FK ids (all optional, so old clients keep working)
- Changed: `UpdatePropertyCommand.cs` / `UpdatePropertyCommandHandler.cs` — same, as a partial-update
- Changed: `PropertyFilterDto.cs` — added the four filter fields
- Changed: `PropertyRepository.cs` — extracted the entire filter-building block out of `GetPagedAsync` into a new **public static** `ApplyFilter(IQueryable<Property>, PropertyFilterDto)`, and added filtering on the four new fields. This extraction is what lets Task 3's background matcher reuse the exact same search rules instead of drifting from them over time.

**Not done in this pass:** the Angular frontend has no UI for governorate/district/
neighborhood/property-type selection yet (confirmed — no references to
`governorateId` anywhere in `HudhudNest/src`). The backend is ready; the
frontend property-form and search-filter components still need cascading
dropdowns wired to `GET /api/lookups` (already exists) and the new fields.

## 3. Task 4 — Advisory duplicate-listing check

- New: `HudhudNestApi.Application/Listings/DTOs/DuplicateCheckResultDto.cs`
- New: `HudhudNestApi.Application/Listings/Queries/CheckPotentialDuplicateProperty/` (Query + Handler)
- Changed: `IPropertyRepository.cs` / `PropertyRepository.cs` — added `FindPotentialDuplicatesAsync(...)`
- New endpoint: `GET /api/properties/check-duplicate?neighborhoodId=&area=&price=&listingType=`
- New tests: `tests/HudhudNestApi.Application.Tests/Listings/CheckPotentialDuplicatePropertyQueryHandlerTests.cs`

Implemented as a **Query**, never a `CreatePropertyCommandValidator` rule — see
the XML doc on `CheckPotentialDuplicatePropertyQuery` for the reasoning
(`ValidationBehavior` collects every FluentValidation failure and unconditionally
throws if there is at least one; there is no "warning" severity tier, so a
probabilistic duplicate signal must never be a blocking validator rule).

Tolerance: ±10% price/area for same-owner matches (likely repost), ±5% for
different-owner matches (possible fraud/duplicate). Requires `neighborhoodId`
— without a structured location there is no reliable way to compare listings
(text `City`/`Region` matching would produce too many false positives/negatives).

Also confirmed during research: phone numbers are unique per user account
(`PhoneNumberLookupHash` has a unique index), so "same phone number across
different owner accounts" — the originally proposed signal — is architecturally
impossible under the current schema. Neighborhood + area + price was used instead.

**Not done in this pass:** frontend call from `property-form` before final submit,
plus the non-blocking confirm dialog UI.

## 4. Task 2 — Price history + "still available?" confirmation

- New entity: `HudhudNestApi.Domain/Listings/Entities/PropertyPriceHistory.cs` (append-only)
- New: `PropertyPriceHistoryConfiguration.cs`, `IPropertyPriceHistoryRepository.cs` / `PropertyPriceHistoryRepository.cs`
- Changed: `UpdatePropertyCommandHandler.cs` — writes one history row per price field that actually changed (`ColdRent`/`WarmRent`/`PurchasePrice`), in the **same `SaveChangesAsync`** as the property update itself — not inside the notification's try/catch, so history persists even if the SignalR push fails
- New: `Property.LastConfirmedAvailableAt` + `Property.ConfirmStillAvailable()` domain method
- New: `ConfirmPropertyAvailabilityCommand` / Handler (mirrors `PublishPropertyCommand`'s shape)
- New endpoint: `PATCH /api/properties/{id}/confirm-availability`
- New tests: `ConfirmPropertyAvailabilityCommandHandlerTests.cs`, `PropertyPriceHistoryTests.cs`

**Not done in this pass:** frontend "last updated" / 30-day staleness banner on
the property detail page, and a "still available?" button that calls the new
endpoint. Also not done: a read endpoint for the price-history timeline itself
(`IPropertyPriceHistoryRepository` currently only has `AddAsync` — add a query
method + endpoint when the frontend needs to display the timeline).

## 5. Task 3 — Saved searches + match alerts

- New entity: `HudhudNestApi.Domain/Search/Entities/SavedSearch.cs` (mirrors `PropertyFilterDto`'s filterable fields)
- New: `SavedSearchConfiguration.cs`, `ISavedSearchRepository.cs` / `SavedSearchRepository.cs`
- New CQRS slice: `HudhudNestApi.Application/Search/{Commands/CreateSavedSearch,Commands/DeleteSavedSearch,Queries/GetMySavedSearches}` (mirrors the `Favorites` module structure)
- New controller: `SavedSearchController.cs` (mirrors `FavoritesController.cs`)
- New: `NotificationType.SavedSearchMatch = 12` (appended, does not renumber existing values — `Notification.Type` is stored as a plain int, not string-converted, confirmed in `NotificationConfiguration.cs`)
- New: `INotificationService.NotifySavedSearchMatchAsync(...)` + implementation
- New: `SavedSearchMatchHostedService : BackgroundService` (mirrors `OtpCleanupHostedService`'s scope-factory + polling-loop pattern, 15-minute interval), registered via `AddHostedService<>()`
- New tests: `tests/HudhudNestApi.Application.Tests/Search/SavedSearchCommandHandlerTests.cs`

The matcher reuses `PropertyRepository.ApplyFilter` (see Task 0) so a saved
search alert can never disagree with what a manual search for the same
criteria would return. Each run only looks at properties `CreatedAt` after the
search's `LastMatchedAt` (or `CreatedAt` on first run) — so a newly created
saved search is never flooded with every pre-existing match at once, and a
match is never sent twice.

**Not done in this pass:** frontend "save this search" button + a
`saved-searches` page (mirror `favorites.page.ts/html`). Per the original
Phase-0 plan, no frontend notification-pipeline changes are needed for the new
`NotificationType` value — `notification.model.ts` types `Type` as a plain
`string`.

## 6. What you need to run yourself

```bash
# 1. Pull these changes into your working tree (already done if you're reading
#    this from the repo — device_commit_files wrote everything in place).

# 2. Restore + build the whole solution (this sandbox could not do this step at all):
dotnet restore
dotnet build

# 3. Generate migrations — one per schema change, so each is reviewable and
#    revertible independently. Do NOT hand-write these; let the tool diff
#    against the (untouched) AppDbContextModelSnapshot.cs:
dotnet ef migrations add ConvertTransactionFieldsToEnums --project HudhudNestApi.Infrastructure --startup-project HudhudNestApi
dotnet ef migrations add AddPropertyPriceHistoryAndAvailabilityConfirmation --project HudhudNestApi.Infrastructure --startup-project HudhudNestApi
dotnet ef migrations add AddSavedSearches --project HudhudNestApi.Infrastructure --startup-project HudhudNestApi

# Review each generated migration's Up()/Down() before applying — in particular
# confirm the Transaction migration does NOT silently drop/truncate any existing
# row data if your Transactions table already has rows (it should be empty today,
# per the "dead code" finding above, but re-verify before running in an
# environment that might not be).

dotnet ef database update --project HudhudNestApi.Infrastructure --startup-project HudhudNestApi

# 4. Run the full test suite:
dotnet test

# 5. Full solution build sanity check — this pass only reviewed the 4 core
#    projects (Domain/Application/Infrastructure/API) plus
#    HudhudNestApi.Application.Tests. It did NOT review HudhudNestApi.Integration.Tests,
#    HudhudNestApi.Auth.Tests, HudhudNestApi.Architecture.Tests, or the other test/tool
#    projects in the .sln for any other class that might implement
#    IPropertyRepository, IUnitOfWork, or INotificationService and would need the
#    same "add the new interface member" fix already applied to
#    PropertyOwnershipServiceTests.cs. Search for `: IPropertyRepository`,
#    `: IUnitOfWork`, and `: INotificationService` across the whole solution
#    before trusting a green build.
```

## 7. Angular frontend follow-up (done in a later pass)

Everything listed as "not done in this pass" above for the frontend has since
been implemented against the real `HudhudNest` repo, in a separate
session, using the same staged-edit-and-commit workflow (files staged from
your machine, edited, committed back). **Same limitation as section 0
applies, and is arguably more important here: this sandbox has no
`ng build`/`ng test`/`npm run build` available and none was attempted, so
none of the TypeScript/HTML/SCSS below has been compiler-verified. Treat it
as a reviewed draft, not a verified change, until you run your own Angular
build and exercise the UI.**

### 7.1 Backend addition required to unblock the frontend

Task 0 above ("Structured location wiring") wired the FK columns and
filtering through the Application/Infrastructure layers, but left
`LookupsController` with **zero endpoints** to actually fetch
governorates/districts/neighborhoods/property types — a real gap discovered
while building the dropdowns, not anticipated in the original plan. Fixed by
adding, to the existing `LookupsController` / `ICommonLookupService` /
`CommonLookupService`:

- `GET /api/lookups/governorates?countryCode=SY` — cached (10 min TTL, same
  `GetOrCreateAsync` pattern as the other lookups), keyed per country code.
- `GET /api/lookups/districts?governorateId=` — **not cached** (parent-scoped;
  `LookupCacheKeys` intentionally has no cache-by-prefix support today, so
  caching every governorate's district list individually was skipped rather
  than half-implemented — worth revisiting if this endpoint gets hot).
- `GET /api/lookups/neighborhoods?districtId=` — same, not cached.
- `GET /api/lookups/property-type-catalog` — cached.
- New DTOs: `StructuredLocationLookupDto`, `PropertyTypeLookupDto`.
- Also fixed a pre-existing EF warning caught while regenerating the earlier
  migration: `PropertyPriceHistoryConfiguration` was missing the same
  `HasQueryFilter(h => !h.Property!.IsDeleted)` guard every other
  `Property`-dependent configuration already has — added for consistency.

These four endpoints need a migration/build/test pass of their own (they're
pure read queries against existing tables, so no schema change, but they are
new code you have not compiled).

### 7.2 Frontend features implemented

- **Structured location dropdowns** — cascading governorate → district →
  neighborhood selects, plus a property-type select, added to both
  `property-form` (create/edit) and `property-list` (search filters).
  `property-form` uses reactive-forms `valueChanges` subscriptions
  (`emitEvent: false` guards to avoid clobbering restored values on edit);
  `property-list` uses plain `(ngModelChange)` handlers, matching each file's
  existing pattern rather than unifying them.
- **Duplicate-listing advisory check** — on create (not edit) submit, if a
  neighborhood is selected, calls the new `GET /properties/check-duplicate`
  and shows a non-blocking modal listing potential matches before the user
  confirms or goes back to edit. Falls through to normal submit on any error
  — this must never block a real submission.
- **"Still available?" / staleness banner** — property-detail page now shows
  an amber banner when the property's last freshness signal
  (`lastConfirmedAvailableAt` ?? `updatedAt` ?? `createdAt`) is more than 30
  days old, visible to all visitors; the owner additionally sees a button that
  calls `PATCH /properties/{id}/confirm-availability` and updates the
  timestamp locally on success.
- **Saved searches** — new `/saved-searches` page (mirrors `my-listings`),
  reachable from the navbar; a 🔔 "save this search" button on `property-list`
  (visible only when logged in) POSTs the current filter state; "run" on a
  saved search navigates to `/properties` with the filters as query params,
  read once via `ActivatedRoute.snapshot.queryParamMap` on init.

### 7.3 Known gaps / things to check when you build

- No `.btn` / `.btn--*` class definitions could be found anywhere in the
  staged files (`property-list.page.scss`, `styles.scss`, `angular.json`'s
  global styles) despite being used throughout the existing templates. New
  buttons were given `.btn--outline` to match existing usage in the same
  templates, but if those classes genuinely don't exist anywhere, they (and
  the new buttons) may render unstyled — worth a quick visual check.
- i18n keys were added to `public/i18n/{ar,en,de}.json` via scripted edits and
  validated as well-formed JSON after each change, but not checked for
  translation-key collisions with anything added elsewhere in parallel.
- Run `ng build` / `ng test` (or your project's equivalent, e.g.
  `npm run build`) yourself and report back any errors — I'll fix them in
  order, same as the backend workflow above.

## 9. Structured-location fix pass — mandatory governorate/district, empty-lists root cause

Triggered by real user-facing feedback: after section 7's frontend work shipped,
the property-form still showed City/Region as free text (marked required) next
to four *optional* dropdowns whose lists were empty. Investigation turned up
three separate, real bugs, not one:

1. **`DatabaseSeeder.SeedAsync` was dead code.** It seeded governorates,
   districts, property types, currencies, and roles — but was never called
   from anywhere in `Program.cs`. Fixed by wiring it in right after
   `app.Build()`, wrapped so a seeding failure logs an error instead of
   crashing the app (every individual seed method is an idempotent
   "ensure exists" per row, so running it on every startup is safe).
2. **`PropertyDto`/`PropertyMapper` never exposed `GovernorateId`/
   `DistrictId`/`NeighborhoodId`/`PropertyTypeId`/`LastConfirmedAvailableAt`.**
   Even with data seeded, editing an existing listing would never have
   restored its saved governorate/district/neighborhood/property-type
   selections, and the property-detail staleness banner (section 4) would
   never have shown — both silently broken since the fields simply weren't in
   the API response. Fixed by adding them to both files.
3. **Existing district data was wrong, not just incomplete.** The original
   `GovernoratesSeed.cs` seeded Damascus *city quarters* (Mazzeh, Kafr Sousa,
   etc.) as "Districts" directly under Damascus governorate, and filed
   Jaramana there too — Jaramana administratively belongs to Rif Dimashq
   governorate, not Damascus. Fixed by restructuring to the real
   administrative tiers: Governorate → District (official, ~65 total, one
   research pass — see below) → Neighborhood (informal city quarters, kept
   separate in a new `NeighborhoodsSeed.cs`).

**Research and its honest limits** (a dedicated research pass was run and is
worth recording so the gap doesn't get "fixed" with guessed data later): the
14 governorates and their ~65 official districts are well-documented
(Wikipedia's "Districts of Syria", cross-checked against May-2026 governor
appointments for any post-Dec-2024 reorganization — none found) and are now
fully seeded. **Neighborhood-level data is a different story: no complete,
verifiable source exists for Syria as a whole.** After checking Wikipedia and
secondary references, only three cities had reasonably sourced lists —
Damascus, Homs, and Latakia — and even those are "reasonable working lists,"
not authoritative registries. The other 11 governorates' districts have zero
seeded neighborhoods, and that's deliberate: inventing plausible-sounding
neighborhood names would silently mislead users, which is worse than an empty
list. `NeighborhoodsSeed.cs` documents exactly this in its file header.

**Resulting product decision** (made with the user after presenting this
finding): Governorate and District are now mandatory on create — full
verified coverage everywhere. Neighborhood stays optional everywhere, with
a manual free-text fallback (`NeighborhoodId` when it's in the seeded list,
else `NeighborhoodText`) for the 11 governorates with no neighborhood data.
Property type is also now mandatory (full 14-item catalog, no coverage gap).

**What changed, concretely:**

- Backend: `GovernoratesSeed.cs` rewritten (real districts, correct Jaramana
  placement); new `NeighborhoodsSeed.cs` (Damascus/Homs/Latakia only);
  `DatabaseSeeder.SeedAsync` wired into `Program.cs`; new
  `Property.NeighborhoodText` column (nullable, needs a migration — see
  section 6); `CreatePropertyCommandValidator` now requires
  GovernorateId/DistrictId/PropertyTypeId (Update stays partial-update/
  optional, matching the existing pattern for other required-on-create
  fields like Title); `PropertyDto`/`PropertyMapper` fixed per point 2 above.
- Frontend (`property-form` only — `property-list`'s search filters were
  deliberately left as optional, since "no filter set" correctly means "any"
  there): City/Region text inputs removed from the visible form (still sent
  to the backend, now auto-derived from the selected governorate/district
  name instead of typed); Country converted from free text to a select;
  Governorate/District/PropertyType selects marked required with inline
  validation messages; Neighborhood select gained a manual-entry text input
  shown whenever the selected district has no seeded neighborhoods, the two
  kept mutually exclusive (picking one clears the other).
- **Still not compiler-verified**, same caveat as section 7 — this was built
  and reviewed manually, no `ng build` was run in this pass either.

**New migration needed**: `AddPropertyNeighborhoodText` (or fold it into
whichever of section 6's three pending migrations you generate last — it's
a single nullable varchar column, low risk either way). Re-run
`dotnet ef migrations add ... --context AppDbContext` per section 6's
pattern, review the generated `Up()`, then `dotnet ef database update`.
After that, restart the API once so `DatabaseSeeder` actually populates the
governorates/districts/neighborhoods/property-types tables — until you do,
the dropdowns will still show empty, migration or not.

## 10. Location suggestions — crowd-sourced coverage with admin moderation

Follow-up to section 9: after wiring the seed data and restarting, the user
reported the lists appeared but were still incomplete (expected — ~65
districts covers the country, but some small localities are inevitably
missing, and neighborhoods only exist for 3 cities). Rather than keep
guessing at more names, the request was to let users type a name that isn't
listed and have it enter an admin-reviewed queue before becoming a real,
selectable entry — turning the seed-data gap into a self-healing process
instead of a permanent dead end.

**New domain concept**: `LocationSuggestion` (Pending/Approved/Rejected).
Property gained `DistrictText` (mirroring the existing `NeighborhoodText`
from section 9) — so **District is now: pick from the list (`DistrictId`) OR
type a name (`DistrictText`)**, same pattern as Neighborhood. At least one is
required (`CreatePropertyCommandValidator`); both is technically allowed
(`DistrictId` wins downstream) but the frontend keeps them mutually exclusive
in the UI.

**Flow**: user types a district/neighborhood name that isn't in the dropdown
→ `CreatePropertyCommandHandler`/`UpdatePropertyCommandHandler` sees
`*Text` set with no matching `*Id` → auto-submits a `LocationSuggestion` via
`ILocationSuggestionService` (deduped against existing Pending suggestions
for the same name+parent, so 10 listings with the same manual name don't
spam the queue) → **this is fire-and-forget relative to the listing save**:
wrapped in its own try/catch, logs on failure, never blocks or fails the
actual property create/update. An admin opens `/admin/location-suggestions`
(new page, linked from the existing `/admin/messages` admin nav — same
`[Authorize(Roles = Admin)]` pattern as `AdminController`), reviews each
suggestion with its submitter and (if any) the listing that triggered it,
and Approves or Rejects. **Approving creates the real District/Neighborhood
row from the submitted name** (Ar and En both set to the submitted text —
there's no bilingual input in the quick moderation UI; an admin can rename
via direct DB edit later if a proper English name is wanted) **and
backfills**: any other property with a matching manual text (same parent,
case-insensitive) gets switched to reference the new ID and its `*Text`
cleared — those listings "graduate" out of manual text retroactively.

**New backend files**: `LocationSuggestion.cs` (domain),
`LocationSuggestionConfiguration.cs`, `ILocationSuggestionService.cs` /
`LocationSuggestionService.cs`, `LocationSuggestionDto.cs`,
`LocationSuggestionsController.cs` (`GET/POST /api/admin/location-suggestions/*`).
**Changed**: `Property.cs` (+`DistrictText`), `PropertyConfiguration.cs`,
`PropertyDto.cs`/`PropertyMapper.cs` (+`DistrictText`),
Create/UpdatePropertyCommand(Handler) (+`DistrictText`, calls the new
service), both validators (District requirement is now cross-field:
`DistrictId` OR `DistrictText`), `LookupInfrastructureRegistration.cs`
(DI registration), `AppDbContext.cs` (+`DbSet<LocationSuggestion>`).

**New frontend files**: `location-suggestion.model.ts`,
`location-suggestions-api.service.ts`, `admin/location-suggestions/
location-suggestions.page.ts` (new admin page, route added to
`app.routes.ts`, link added to `admin/messages` template). **Changed**:
`property-form.page.ts`/`.html` — District is now select-or-manual-text like
Neighborhood already was, with the same mutual-exclusivity behavior;
`property.model.ts` (+`districtText`).

**New migration needed** (in addition to section 9's
`AddPropertyNeighborhoodText`): a `LocationSuggestions` table plus
`Properties.DistrictText`. Generate both in the same
`dotnet ef migrations add` pass if you haven't run section 9's migration
yet — EF diffs the whole model, so running migrations add now (instead of
twice) collapses them into one migration; review the generated `Up()` as
usual before applying.

**Not done in this pass** (deliberately, to keep this change reviewable):
no email/notification to the admin when a new suggestion arrives (they have
to check `/admin/location-suggestions` manually); no bilingual (Ar/En) input
in the approval UI, so approved entries get the same text in both name
columns until an admin manually improves the English name; no rate-limiting
on suggestion submission beyond the same-name dedup (a malicious user could
still submit many *different* junk names — acceptable for now since it just
grows the moderation queue, doesn't affect actual listing data, and this
app doesn't have open public signup abuse at its current scale).

## 8. Explicitly out of scope

- Any change to Auth/Identity (per the original ground rules).
- A read/query endpoint for the price-history timeline (write-only for now;
  no frontend timeline UI either).
- Neighborhood/district coverage beyond what's seeded — see section 9 for
  what's seeded and section 10 for how gaps now get filled (admin-reviewed
  suggestions) instead of more guessed names.
- Caching for the new `districts`/`neighborhoods` lookup endpoints (see 7.1).
