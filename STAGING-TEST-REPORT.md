# HUDHUDNEST -- Staging End-to-End Synthetic Data Test Report

## Executive Summary

| Item | Value |
|---|---|
| Test date | 2026-10-08 (UTC) |
| Batch id | HUDHUDNEST-STAGING-QA-2026-10 |
| Environment | STAGING (proven via /api/operational/build-info: environment=Staging, notifications disabled, in-memory media off, redis/storage isolation markers contain "staging", 67/67 migrations applied) |
| Commit SHA (API deployed) | ec17ea9df1844af92c7bc5989992695d5f52df58 |
| API version | 0.1.0-staging |
| Frontend | https://staging--hudhudnest.netlify.app (version not exposed to a non-admin; STAGING badge visible) |
| Database | Render Postgres, isolation marker rtmlqjhfvlxmdovtpono (not directly queried: no DB credentials were used) |
| Accounts created | 103 (100 owners, 1 probe, 2 repro) |
| Listings created | 100 via the real API (+1 probe, +2 repro, all identified by owner) |
| Images uploaded | 411 through the real upload endpoint (+ a few in probe/repro) |
| Cities (governorates) | 12: Damascus 9, Rural Damascus 9, Aleppo 9, Homs 9, Hama 8, Latakia 8, Tartus 8, Deir ez-Zor 8, Raqqa 8, Idlib 8, Daraa 8, As-Suwayda 8 |
| Property types | all 13 existing types: apartment 30, villa 10, studio 5, house 5, residential-land 5, hotel-apartment 5, agricultural-land 5, shop 10, office 5, commercial-land 5, warehouse 5, industrial-land 5, hotel 5 |
| Listing types | ForRent 30, ForSale 55, ForRentAndSale 15; Short stay and Investment: NOT created (see Blocked) |
| Currencies | USD 84, EUR 11, SYP 5 |
| Content languages | ar 70, en 20, de 10 |
| Tests executed | 175 (API 162 + browser 13) |
| Passed / Failed / Blocked | 166 / 6 / 3 |
| Issues | P0 0, P1 2, P2 1, P3 4 |

Production was never contacted. All data lives in Staging; image files are stored under the `staging/` Cloudinary folder.

## Safety gate (Phase 0)

STAGING SAFETY CHECK: PASS. Every script re-runs the gate (build-info with the Staging automation secret) before any write and aborts on any mismatch.

## What was done

1. Registered 103 synthetic accounts through the real phone-OTP flow (reserved test phone prefix, fixed Staging OTP), 0 failures.
2. Created 100 listings (one per owner) with deterministic, varied data and uploaded 411 generated PNG images (0 / 1 / 3 / 4 / 6 / 10 per listing; no real people).
3. Verified create -> persist -> retrieve -> search -> filter -> display -> interact -> authorize -> measure over the API and in the browser (desktop 1280, tablet 768, mobile 375; AR/EN/DE).
4. Clean-room reproductions for the image defects.

## Test Summary Matrix

| Area | Tests | Passed | Failed | Blocked | Severity |
|---|---:|---:|---:|---:|---|
| Registration | 5 | 4 | 0 | 1 | - |
| Login | 13 | 13 | 0 | 0 | - |
| Listings | 8 | 7 | 0 | 1 | - |
| Images | 18 | 14 | 4 | 0 | P1 |
| Search | 8 | 8 | 0 | 0 | P2 |
| Filters | 22 | 21 | 1 | 0 | P2 |
| Sorting | 6 | 6 | 0 | 0 | - |
| Pagination | 9 | 9 | 0 | 0 | - |
| Authorization | 19 | 19 | 0 | 0 | - |
| Favorites | 11 | 10 | 0 | 1 | - |
| Contact | 8 | 8 | 0 | 0 | - |
| Dashboard | 1 | 1 | 0 | 0 | - |
| Mobile | 3 | 3 | 0 | 0 | - |
| RTL | 2 | 2 | 0 | 0 | - |
| Performance | 11 | 11 | 0 | 0 | - |
| API | 18 | 18 | 0 | 0 | - |
| Security | 12 | 11 | 1 | 0 | P3 |
| Randomized | 1 | 1 | 0 | 0 | - |

Failed tests:
- I13b (Images): delete MAIN image while others exist -- HTTP 500 corr a51da140e62e6e5bc0dc071030417ae5
- I13c (Images): delete the only image, then upload a new one -- HTTP 500 (and again 500)
- I15 (Images): 21 MB upload rejected (413/400) -- HTTP 502
- EXP-/swagger/v1/swagger.json (Security): /swagger/v1/swagger.json not publicly exposed -- HTTP 200
- UI10 (Images): No-image placeholder is localized -- 
- UI11 (Filters): Province/district/property-type labels translated in the German UI -- 

Blocked tests:
- SS2 (Listings): short-stay & investment creation by synthetic users
- UI12 (Registration): UI registration/login via the browser form
- UI13 (Favorites): Favorites through the UI (needs an authenticated browser session)

Additionally blocked / not covered (see Limitations): listing statuses Draft/Pending/Rejected do not exist; direct database integrity queries; direct multi-listing-per-account distribution (free-plan quota).

## Issues

```text
BUG-ID: STG-001

Severity:
P1

Area:
Listing images

Title:
Deleting the main image while other images exist returns HTTP 500

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
Owner account on the free plan with one listing holding 3 uploaded images (image #1 is main).

Steps to Reproduce:
1. Upload 3 images to own listing (200).
2. PATCH .../images/{secondImageId}/setmain (204) so a known image is main.
3. DELETE /api/properties/{id}/images/{mainImageId}.

Expected:
204; the next image (lowest SortOrder) becomes main.

Actual:
500 {"title":"Internal Server Error","message":"An unexpected error occurred..."}; the image stays, nothing changes.

Evidence:
staging-test-evidence/repro-main-image.json (S1 DELETE, correlation id a51da140e62e6e5bc0dc071030417ae5); test I13b.

HTTP Status:
500

Affected Users:
Every owner who deletes the main photo of a multi-photo listing.

Security Impact:
None.

Performance Impact:
None.

Suggested Fix:
PropertyImage is soft-deleted (AppDbContext query filter !IsDeleted) but keeps IsMain=true, and unique index IX_PropertyImages_OneMainPerProperty (PropertyId, IsMain) WHERE IsMain=true ignores IsDeleted. DeletePropertyImageCommandHandler promotes the replacement in the same SaveChanges, so two rows hold IsMain=true. Fix: set image.IsMain=false when removing it (and save before promoting, or in one UPDATE order), and/or add AND "IsDeleted" = false to the index filter via a migration. Root cause derived from code at the staging commit; not confirmed in server logs.

Regression Test Required:
YES
```

```text
BUG-ID: STG-002

Severity:
P1

Area:
Listing images

Title:
After deleting the only/main image, every later upload to that listing fails with HTTP 500 (listing is stuck without photos)

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
Owner with a listing that has exactly one image.

Steps to Reproduce:
1. Upload 1 image (200).
2. DELETE that image (204).
3. Upload a new image to the same listing.
4. Upload again.

Expected:
200 and the new image becomes main.

Actual:
500 on every attempt (reproduced twice on a clean account, and observed on a second listing). GET images returns an empty list.

Evidence:
staging-test-evidence/repro-main-image.json (S2 steps); tests I13c. Listing 5f282dc5-8285-4174-b1d6-70f7fdc340ae (QA probe account) is intentionally left in this broken state.

HTTP Status:
500

Affected Users:
Any owner who removes a listing's only photo and wants to add another.

Security Impact:
None.

Performance Impact:
None.

Suggested Fix:
Same root cause as STG-001: UploadPropertyImagesCommandHandler counts only non-deleted images, sees 0, assigns IsMain=true, and collides with the soft-deleted main image on the unique index. Same fix; also add a regression test that deletes the last image then uploads. The listing can only be repaired by data fix (clear IsMain on the soft-deleted row).

Regression Test Required:
YES
```

```text
BUG-ID: STG-003

Severity:
P2

Area:
Search / Filters

Title:
Listings of type ForRentAndSale are excluded when filtering by either "For rent" or "For sale"; the UI has no combined option

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
15 of the 100 batch listings are ForRentAndSale.

Steps to Reproduce:
1. GET /api/properties?ListingType=ForSale and ?ListingType=ForRent.
2. Open /properties and use the Anzeigenart toggle (All / For rent / For sale).

Expected:
A "for sale or rent" listing matches a buyer searching for sale and a tenant searching for rent (or the product defines it as separate).

Actual:
Filter is exact-match: those listings only appear under "All". The three counts (30 rent, 55 sale, 15 combined) add up to 100 with no overlap.

Evidence:
tests F-lt-ForRent / F-lt-ForSale (oracle exact-match passes); UI toggles list only two options.

HTTP Status:
200

Affected Users:
Buyers/tenants using the type filter.

Security Impact:
None.

Performance Impact:
None.

Suggested Fix:
Product decision. If combined listings should be discoverable: treat ForRentAndSale as matching both ListingType=ForRent and ListingType=ForSale in the query (and show a badge). Otherwise document it.

Regression Test Required:
YES (once decided)
```

```text
BUG-ID: STG-004

Severity:
P3

Area:
Security / Exposure

Title:
Swagger JSON is publicly readable on Staging (/swagger/v1/swagger.json -> 200)

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
Anonymous client.

Steps to Reproduce:
1. GET https://propertyapi-staging-api.onrender.com/swagger/v1/swagger.json

Expected:
Not exposed outside Development, or protected.

Actual:
200 with the OpenAPI document.

Evidence:
test EXP-/swagger/v1/swagger.json.

HTTP Status:
200

Affected Users:
n/a

Security Impact:
Discloses the full API surface to anonymous users. Production was NOT probed (out of scope: Production must not be touched); verify it separately.

Performance Impact:
None.

Suggested Fix:
Disable Swagger (or require auth) in Staging/Production; keep it for Development only.

Regression Test Required:
YES
```

```text
BUG-ID: STG-005

Severity:
P3

Area:
Images / Edge

Title:
A 21 MB image upload returns HTTP 502 instead of 413/400

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
Authenticated owner; body larger than the 20 MB request limit.

Steps to Reproduce:
1. POST /api/properties/{id}/images with a 21 MB PNG part.

Expected:
413 or 400 with a clear message (the action has [RequestSizeLimit(20_000_000)]).

Actual:
502 (connection closed by the proxy/edge before the app answered).

Evidence:
test I15.

HTTP Status:
502

Affected Users:
Users uploading oversize files.

Security Impact:
None.

Performance Impact:
None.

Suggested Fix:
Confirm which layer closes the connection (Render edge vs Kestrel). Add a client-side size check; consider returning 413 from the app for >5 MB per file (handler limit) before streaming.

Regression Test Required:
NO
```

```text
BUG-ID: STG-006

Severity:
P3

Area:
UI / i18n

Title:
No-image placeholder text "No image" is not localized (shown in English in the German UI)

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
Listing without images (e.g. batch listing #19), German UI, 375px.

Steps to Reproduce:
1. Open /properties/072223f7-60e6-4e6d-a043-fc6542dd9c54.

Expected:
Localized text (DE/AR).

Actual:
Placeholder reads "No image".

Evidence:
Browser pane screenshot during UI test (not saved to disk).

HTTP Status:
200

Affected Users:
DE/AR users.

Security Impact:
None.

Performance Impact:
None.

Suggested Fix:
Add the placeholder label to the i18n files.

Regression Test Required:
NO
```

```text
BUG-ID: STG-007

Severity:
P3

Area:
UI / i18n

Title:
Province, district and property-type labels are English in the German UI

Environment:
STAGING

Commit:
ec17ea9df1844af92c7bc5989992695d5f52df58

Preconditions:
German UI, /properties filter panel.

Steps to Reproduce:
1. Open the filters.

Expected:
German labels (the data has nameAr/nameEn only).

Actual:
Labels such as "Damascus", "Apartment", "Hotel Apartment" appear in English next to German headings.

Evidence:
get_page_text of /properties filter panel; /api/lookups/* return nameAr/nameEn only.

HTTP Status:
200

Affected Users:
German-speaking users.

Security Impact:
None.

Performance Impact:
None.

Suggested Fix:
Add German names to lookups or fall back explicitly with a documented policy.

Regression Test Required:
NO
```

## Observations (not defects, or needing a decision)

- Staging runs commit ec17ea9 (origin/master); the local checkout used for code reading was 135658b. The image-handling files cited above are unchanged between the two commits.
- Free plan allows exactly one active listing per account (HTTP 409 with a clear message); paid plans are requested as "pending" (202) and are not self-activating, so no payment bypass exists. This made the "5 listings per account" distribution impossible: the batch uses 100 accounts x 1 listing.
- Creating a listing publishes it immediately (response status "Published", nextAction "UploadImages"); a listing can therefore be public with no image. 3 batch listings were intentionally created without images and are readable.
- Public listing JSON includes ownerId and ownerName (the owner's display name). For seeded real users that is a real name. Confirm this is intended.
- Staging runs with forwarded headers disabled, so all callers share one IP rate-limit bucket (120 req/min on public-read). A burst of tests produced HTTP 429 on /api/security/csrf-token and /api/properties/{id}/images. The limiter works; but verify on Production that the real client IP is used, otherwise all visitors share one bucket.
- The browser console shows one 401 on the anonymous home page (resource not identified).
- The database-name marker reported by build-info (rtmlqjhfvlxmdovtpono) does not itself contain the word "staging"; isolation was proven by environment=Staging, redis/storage markers containing "staging", notifications disabled, and image paths under staging/.

## Performance (Staging, single client from one location, Render free tier)

| Scenario | Requests | Avg ms | P95 ms | P99 ms | HTTP status counts | 5xx |
|---|---:|---:|---:|---:|---|---:|
| GET /api/properties?pageSize=20 | 30 | 188 | 417 | 425 | {"200":30} | 0 |
| GET /api/properties (filters combo) | 30 | 199 | 481 | 495 | {"200":30} | 0 |
| GET /api/properties/{id} | 40 | 351 | 360 | 438 | {"200":40} | 0 |
| GET /api/properties/{id} (conc 5) | 50 | 267 | 408 | 775 | {"200":50} | 0 |
| GET /api/properties?SearchTerm | 20 | 181 | 413 | 422 | {"200":20} | 0 |
| GET /api/lookups/governorates | 20 | 134 | 141 | 213 | {"200":20} | 0 |
| GET /api/properties/{id}/images | 20 | 206 | 275 | 284 | {"200":10,"429":10} | 0 |
| GET /api/properties/mine (auth) | 15 | 235 | 254 | 254 | {"200":15} | 0 |
| GET /api/users/me (auth) | 15 | 494 | 507 | 507 | {"200":15} | 0 |

All requests observed during the whole run, grouped by endpoint (includes correlated test traffic and expected 4xx):

| Endpoint | Requests | Avg ms | P95 ms | P99 ms | HTTP >= 400 |
|---|---:|---:|---:|---:|---:|
| DELETE /api/favorites/{id} | 109 | 235 | 299 | 302 | 101 |
| GET /api/properties/{id} | 95 | 308 | 401 | 775 | 0 |
| GET /api/properties | 86 | 213 | 512 | 526 | 0 |
| GET /api/properties/{id}/images | 22 | 214 | 291 | 299 | 10 |
| GET /api/users/me | 22 | 491 | 505 | 507 | 0 |
| GET /api/lookups/governorates | 20 | 134 | 141 | 212 | 0 |
| POST /api/properties | 19 | 423 | 1372 | 1372 | 16 |
| GET /api/properties/mine | 17 | 238 | 300 | 300 | 0 |
| POST /api/auth/phone/login | 9 | 1144 | 1255 | 1255 | 0 |
| POST /api/properties/{id}/images | 8 | 847 | 3979 | 3979 | 6 |
| POST /api/favorites/{id} | 8 | 369 | 383 | 383 | 0 |
| GET /api/favorites | 6 | 205 | 212 | 212 | 0 |

Image CDN (HEAD, 411 requests): avg 119 ms, P95 141 ms, P99 232 ms; all 411 batch URLs returned 200 image/*.
Read endpoints stay under ~0.5 s P95; login ~1.1 s; image upload up to ~4 s for 10 images. Burst traffic is rate-limited per IP (429) as designed.

## Limitations

- Account distribution: the free plan allows ONE active listing per account and paid plans cannot be self-activated, so "5 listings per account" could not be tested. Ownership/authorization was tested with many owners instead (A/B/C and the probe account).
- Short stay (separate API/flow) and Investment projects (admin-managed) were not created: BLOCKED (no admin credentials; out of scope). Only their public list endpoints were read (200).
- Listing statuses: the system has Available/Reserved/Sold/Rented/Archived/Expired (no Draft/Pending/Rejected). Status transitions were not exercised in this run.
- Browser login/registration forms were not driven (policy: no credential entry on a non-local host). Authentication was tested at API level.
- No direct database access: FK/orphan/duplicate checks were done through API consistency only (counts, ownership, favorites, messages).
- OTP uses the Staging fixed-OTP test mechanism; no real SMS/e-mail was sent.
- Staging built-in cleanup endpoint only accepts <=4 users / <=10 listings with the exact title E2E-SMOKE-<run>; it cannot clean this batch. Cleanup is therefore done via owners' own DELETE calls (script prepared, NOT run).
- Browser screenshots were viewed during testing but not saved to disk; evidence is textual.
- Dashboard statistics (views, inquiries counters) were not available/verified beyond /properties/mine.

## Cleanup status

NOT performed. Data is intentionally retained for manual inspection. Everything created by this test is identifiable by owner (last name contains HUDHUDNEST-STAGING-QA-2026-10) and by the IDs in STAGING-TEST-DATA.md. Run `node staging-qa/cleanup.mjs` (after restoring the Staging secrets file) to delete only these accounts' listings; it verifies each account's last name contains the batch id before deleting. Pre-existing Staging listings (2) were not touched.

## Answers to the required questions

1. Can accounts be created? YES (103/103).
2. Can users log in? YES; wrong password and unknown phone behave identically (no enumeration); logout revokes refresh.
3. Can listings be created? YES, 100/100; quota (free = 1) enforced with 409.
4. Can images be uploaded? YES, 411/411; validation rejects fake/svg/exe/11 files. BUT see STG-001/002 for deletion.
5. Do images display? YES: all 411 URLs 200 image/*, rendered in list, detail gallery (up to 10) and mobile.
6. Do listings appear in search? YES, all 100 found in the public list and by tag search.
7. Do filters work? YES: 21 filter checks (governorate, type, listing type, rooms, area, price, balcony, combos, text search incl. Arabic/German) matched an independent oracle. See STG-003 for the combined-type semantics.
8. Is pagination correct? YES (API pageSize 10 and 100, UI 6 pages, no duplicates/missing; invalid paging -> 4xx).
9. Do cities work? YES, 12 governorates with consistent district hierarchy.
10. Do property types work? YES, all 13.
11. Sale/rent/short stay/investment? Sale, rent and sale-or-rent: YES. Short stay and investment: not creatable by synthetic users (BLOCKED); public reads work.
12. Are owner permissions correct? YES (A could not edit/delete/publish/upload/manage B's listing; mass-assignment of OwnerId ignored on create and update).
13. Is there IDOR? None found in 19 authorization checks.
14. CORS problems? None: Staging origin allowed; Production origin and arbitrary origins not reflected.
15. API problems? Yes: STG-001/002 (500) and STG-005 (502 on oversize).
16. Database problems? No anomalies visible through the API; the unique-index/soft-delete interaction (STG-001/002) is a data-model defect.
17. Storage problems? Isolation OK (all under staging/). Deleted non-main image URL returned 404 after delete.
18. Mobile problems? None found on the pages tested (375px list/detail).
19. RTL problems? None found.
20. Performance problems? None significant; see tables. Image upload of 10 files up to ~4 s.
21. Security problems? No P0/P1 security defect. STG-004 (Swagger exposed on Staging, P3). Auth, CSRF (cookie endpoints), JWT forgery/alg=none, SQL-injection-like input (blocked by edge with 403), XSS-like title stored inertly all passed.
22. Blocks Production: STG-001 and STG-002 (P1).
23. Can be deferred: STG-004..007 (P3) and STG-003 (decision).
24. Fix first: STG-001/002 together (one migration + handler change + regression test), then verify Swagger exposure on Production.

# FINAL VERDICT

```text
FAIL
```

Rationale: the core journey (register -> login -> create -> upload -> publish -> search -> filter -> paginate -> view -> favorite -> contact -> authorize) works on 100 listings with no security defect, but two reproducible P1 defects break owner image management (STG-001, STG-002) and several acceptance items remain BLOCKED. Fix the two P1 items and re-run `staging-qa/repro-main-image.mjs` and `staging-qa/tests.mjs` before promoting to Production.
