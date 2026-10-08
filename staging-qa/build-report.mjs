// Builds STAGING-TEST-REPORT.md, STAGING-TEST-DATA.md, staging-test-results.json and staging-test-evidence/ from results.json + state.json.
import { readFileSync, writeFileSync, mkdirSync, copyFileSync } from 'node:fs';
const st = JSON.parse(readFileSync('state.json', 'utf8'));
const api = JSON.parse(readFileSync('results.json', 'utf8'));
const OUT = '../'; // repo root
const BATCH = 'HUDHUDNEST-STAGING-QA-2026-10';

// ---- manual UI checks (built-in browser against https://staging--hudhudnest.netlify.app) ----
const ui = [
  ['Mobile', 'UI1', 'Listings page 375px (AR and DE): no horizontal overflow, 20 cards, no broken images', 'pass'],
  ['Mobile', 'UI2', 'Listing detail 375px (no-image listing, SYP listing, 3-image gallery): no overflow, price/area/rooms render', 'pass'],
  ['Mobile', 'UI3', 'Listing detail 768px tablet with 10-image gallery (1/10 counter, thumbnails)', 'pass'],
  ['RTL', 'UI4', 'Arabic: dir=rtl, nav/filter/cards mirrored, no overflow (desktop 1280 and mobile 375)', 'pass'],
  ['RTL', 'UI5', 'English and German: dir=ltr, nav and filters translated, no raw translation keys / undefined / null / NaN', 'pass'],
  ['Search', 'UI6', 'UI filter Damascus + Apartment + For sale shows empty state; API agrees (0)', 'pass'],
  ['Search', 'UI7', 'Filter state lives in URL; list -> detail -> browser Back preserves filter and result count (2 = API 2)', 'pass'],
  ['Pagination', 'UI8', 'UI pagination: 6 pages, 20/20/20/20/20/5, 105 unique cards, Next disabled on last page', 'pass'],
  ['Listings', 'UI9', 'Result counter (105) equals API totalCount (100 batch + 2 existing + 1 probe + 2 repro)', 'pass'],
  ['Images', 'UI10', 'No-image placeholder is localized', 'fail'],
  ['Filters', 'UI11', 'Province/district/property-type labels translated in the German UI', 'fail'],
  ['Registration', 'UI12', 'UI registration/login via the browser form', 'blocked'],
  ['Favorites', 'UI13', 'Favorites through the UI (needs an authenticated browser session)', 'blocked'],
];
const rows = [
  ...api.map((r) => ({ area: r.area, id: r.id, name: r.name, status: r.status, detail: r.detail, bug: r.bug })),
  ...ui.map(([area, id, name, status]) => ({ area, id, name, status, detail: status === 'blocked' ? 'BLOCKED BY POLICY: no credential entry on a non-local host; API-level auth/favorites covered instead' : '' })),
];
const sum = (f) => rows.filter(f).length;
const total = rows.length, passed = sum((r) => r.status === 'pass'), failed = sum((r) => r.status === 'fail'), blocked = sum((r) => r.status === 'blocked');

const images = st.listings.reduce((a, l) => a + l.images.length, 0);
const count = (k) => st.listings.reduce((m, l) => ((m[l[k]] = (m[l[k]] || 0) + 1), m), {});
const GOVN = { 1: 'Damascus', 2: 'Rural Damascus', 3: 'Aleppo', 4: 'Homs', 5: 'Hama', 6: 'Latakia', 7: 'Tartus', 9: 'Deir ez-Zor', 10: 'Raqqa', 11: 'Idlib', 12: 'Daraa', 14: 'As-Suwayda' };
const byGov = Object.fromEntries(Object.entries(count('governorateId')).map(([k, v]) => [GOVN[k], v]));

// ---- bugs ----
const bugs = [
  { id: 'STG-001', sev: 'P1', area: 'Listing images', title: 'Deleting the main image while other images exist returns HTTP 500',
    pre: 'Owner account on the free plan with one listing holding 3 uploaded images (image #1 is main).',
    steps: ['Upload 3 images to own listing (200).', 'PATCH .../images/{secondImageId}/setmain (204) so a known image is main.', 'DELETE /api/properties/{id}/images/{mainImageId}.'],
    expected: '204; the next image (lowest SortOrder) becomes main.', actual: '500 {"title":"Internal Server Error","message":"An unexpected error occurred..."}; the image stays, nothing changes.',
    evidence: 'staging-test-evidence/repro-main-image.json (S1 DELETE, correlation id a51da140e62e6e5bc0dc071030417ae5); test I13b.', http: '500', users: 'Every owner who deletes the main photo of a multi-photo listing.',
    sec: 'None.', perf: 'None.',
    fix: 'PropertyImage is soft-deleted (AppDbContext query filter !IsDeleted) but keeps IsMain=true, and unique index IX_PropertyImages_OneMainPerProperty (PropertyId, IsMain) WHERE IsMain=true ignores IsDeleted. DeletePropertyImageCommandHandler promotes the replacement in the same SaveChanges, so two rows hold IsMain=true. Fix: set image.IsMain=false when removing it (and save before promoting, or in one UPDATE order), and/or add AND "IsDeleted" = false to the index filter via a migration. Root cause derived from code at the staging commit; not confirmed in server logs.',
    reg: 'YES' },
  { id: 'STG-002', sev: 'P1', area: 'Listing images', title: 'After deleting the only/main image, every later upload to that listing fails with HTTP 500 (listing is stuck without photos)',
    pre: 'Owner with a listing that has exactly one image.',
    steps: ['Upload 1 image (200).', 'DELETE that image (204).', 'Upload a new image to the same listing.', 'Upload again.'],
    expected: '200 and the new image becomes main.', actual: '500 on every attempt (reproduced twice on a clean account, and observed on a second listing). GET images returns an empty list.',
    evidence: 'staging-test-evidence/repro-main-image.json (S2 steps); tests I13c. Listing 5f282dc5-8285-4174-b1d6-70f7fdc340ae (QA probe account) is intentionally left in this broken state.', http: '500', users: 'Any owner who removes a listing\'s only photo and wants to add another.',
    sec: 'None.', perf: 'None.',
    fix: 'Same root cause as STG-001: UploadPropertyImagesCommandHandler counts only non-deleted images, sees 0, assigns IsMain=true, and collides with the soft-deleted main image on the unique index. Same fix; also add a regression test that deletes the last image then uploads. The listing can only be repaired by data fix (clear IsMain on the soft-deleted row).',
    reg: 'YES' },
  { id: 'STG-003', sev: 'P2', area: 'Search / Filters', title: 'Listings of type ForRentAndSale are excluded when filtering by either "For rent" or "For sale"; the UI has no combined option',
    pre: '15 of the 100 batch listings are ForRentAndSale.',
    steps: ['GET /api/properties?ListingType=ForSale and ?ListingType=ForRent.', 'Open /properties and use the Anzeigenart toggle (All / For rent / For sale).'],
    expected: 'A "for sale or rent" listing matches a buyer searching for sale and a tenant searching for rent (or the product defines it as separate).',
    actual: 'Filter is exact-match: those listings only appear under "All". The three counts (30 rent, 55 sale, 15 combined) add up to 100 with no overlap.',
    evidence: 'tests F-lt-ForRent / F-lt-ForSale (oracle exact-match passes); UI toggles list only two options.', http: '200', users: 'Buyers/tenants using the type filter.', sec: 'None.', perf: 'None.',
    fix: 'Product decision. If combined listings should be discoverable: treat ForRentAndSale as matching both ListingType=ForRent and ListingType=ForSale in the query (and show a badge). Otherwise document it.', reg: 'YES (once decided)' },
  { id: 'STG-004', sev: 'P3', area: 'Security / Exposure', title: 'Swagger JSON is publicly readable on Staging (/swagger/v1/swagger.json -> 200)',
    pre: 'Anonymous client.', steps: ['GET https://propertyapi-staging-api.onrender.com/swagger/v1/swagger.json'], expected: 'Not exposed outside Development, or protected.', actual: '200 with the OpenAPI document.',
    evidence: 'test EXP-/swagger/v1/swagger.json.', http: '200', users: 'n/a', sec: 'Discloses the full API surface to anonymous users. Production was NOT probed (out of scope: Production must not be touched); verify it separately.', perf: 'None.',
    fix: 'Disable Swagger (or require auth) in Staging/Production; keep it for Development only.', reg: 'YES' },
  { id: 'STG-005', sev: 'P3', area: 'Images / Edge', title: 'A 21 MB image upload returns HTTP 502 instead of 413/400',
    pre: 'Authenticated owner; body larger than the 20 MB request limit.', steps: ['POST /api/properties/{id}/images with a 21 MB PNG part.'], expected: '413 or 400 with a clear message (the action has [RequestSizeLimit(20_000_000)]).', actual: '502 (connection closed by the proxy/edge before the app answered).',
    evidence: 'test I15.', http: '502', users: 'Users uploading oversize files.', sec: 'None.', perf: 'None.', fix: 'Confirm which layer closes the connection (Render edge vs Kestrel). Add a client-side size check; consider returning 413 from the app for >5 MB per file (handler limit) before streaming.', reg: 'NO' },
  { id: 'STG-006', sev: 'P3', area: 'UI / i18n', title: 'No-image placeholder text "No image" is not localized (shown in English in the German UI)',
    pre: 'Listing without images (e.g. batch listing #19), German UI, 375px.', steps: ['Open /properties/072223f7-60e6-4e6d-a043-fc6542dd9c54.'], expected: 'Localized text (DE/AR).', actual: 'Placeholder reads "No image".', evidence: 'Browser pane screenshot during UI test (not saved to disk).', http: '200', users: 'DE/AR users.', sec: 'None.', perf: 'None.', fix: 'Add the placeholder label to the i18n files.', reg: 'NO' },
  { id: 'STG-007', sev: 'P3', area: 'UI / i18n', title: 'Province, district and property-type labels are English in the German UI',
    pre: 'German UI, /properties filter panel.', steps: ['Open the filters.'], expected: 'German labels (the data has nameAr/nameEn only).', actual: 'Labels such as "Damascus", "Apartment", "Hotel Apartment" appear in English next to German headings.', evidence: 'get_page_text of /properties filter panel; /api/lookups/* return nameAr/nameEn only.', http: '200', users: 'German-speaking users.', sec: 'None.', perf: 'None.', fix: 'Add German names to lookups or fall back explicitly with a documented policy.', reg: 'NO' },
];
const obs = [
  'Staging runs commit ec17ea9 (origin/master); the local checkout used for code reading was 135658b. The image-handling files cited above are unchanged between the two commits.',
  'Free plan allows exactly one active listing per account (HTTP 409 with a clear message); paid plans are requested as "pending" (202) and are not self-activating, so no payment bypass exists. This made the "5 listings per account" distribution impossible: the batch uses 100 accounts x 1 listing.',
  'Creating a listing publishes it immediately (response status "Published", nextAction "UploadImages"); a listing can therefore be public with no image. 3 batch listings were intentionally created without images and are readable.',
  'Public listing JSON includes ownerId and ownerName (the owner\'s display name). For seeded real users that is a real name. Confirm this is intended.',
  'Staging runs with forwarded headers disabled, so all callers share one IP rate-limit bucket (120 req/min on public-read). A burst of tests produced HTTP 429 on /api/security/csrf-token and /api/properties/{id}/images. The limiter works; but verify on Production that the real client IP is used, otherwise all visitors share one bucket.',
  'The browser console shows one 401 on the anonymous home page (resource not identified).',
  'The database-name marker reported by build-info (rtmlqjhfvlxmdovtpono) does not itself contain the word "staging"; isolation was proven by environment=Staging, redis/storage markers containing "staging", notifications disabled, and image paths under staging/.',
];

const startedAt = st.gate ? new Date(st.testsRanAt).toISOString() : '';
const sev = { P0: 0, P1: bugs.filter((b) => b.sev === 'P1').length, P2: bugs.filter((b) => b.sev === 'P2').length, P3: bugs.filter((b) => b.sev === 'P3').length };

// ---- matrix ----
const areas = ['Registration', 'Login', 'Listings', 'Images', 'Search', 'Filters', 'Sorting', 'Pagination', 'Authorization', 'Favorites', 'Contact', 'Dashboard', 'Mobile', 'RTL', 'Performance', 'API', 'Security', 'Randomized'];
const areaSev = { Images: 'P1', Filters: 'P2', Security: 'P3', Search: 'P2' };
const matrix = areas.map((a) => { const r = rows.filter((x) => x.area === a); return `| ${a} | ${r.length} | ${r.filter((x) => x.status === 'pass').length} | ${r.filter((x) => x.status === 'fail').length} | ${r.filter((x) => x.status === 'blocked').length} | ${areaSev[a] ?? '-'} |`; }).join('\n');
const fails = rows.filter((r) => r.status === 'fail').map((r) => `- ${r.id} (${r.area}): ${r.name} -- ${r.detail}`).join('\n');
const blockedList = rows.filter((r) => r.status === 'blocked').map((r) => `- ${r.id} (${r.area}): ${r.name}`).join('\n');
const perfTbl = (st.perf ?? []).map((p) => `| ${p.endpoint} | ${p.requests} | ${p.avgMs} | ${p.p95Ms} | ${p.p99Ms} | ${JSON.stringify(p.status)} | ${p.errors5xx} |`).join('\n');
const apiTbl = (st.apiTimings ?? []).filter((x) => x.count >= 5).slice(0, 14).map((p) => `| ${p.endpoint} | ${p.count} | ${p.avgMs} | ${p.p95Ms} | ${p.p99Ms} | ${p.non2xx} |`).join('\n');
const bugMd = bugs.map((b) => `\`\`\`text
BUG-ID: ${b.id}

Severity:
${b.sev}

Area:
${b.area}

Title:
${b.title}

Environment:
STAGING

Commit:
${st.gate.commitSha}

Preconditions:
${b.pre}

Steps to Reproduce:
${b.steps.map((s, i) => `${i + 1}. ${s}`).join('\n')}

Expected:
${b.expected}

Actual:
${b.actual}

Evidence:
${b.evidence}

HTTP Status:
${b.http}

Affected Users:
${b.users}

Security Impact:
${b.sec}

Performance Impact:
${b.perf}

Suggested Fix:
${b.fix}

Regression Test Required:
${b.reg}
\`\`\``).join('\n\n');

const report = `# HUDHUDNEST -- Staging End-to-End Synthetic Data Test Report

## Executive Summary

| Item | Value |
|---|---|
| Test date | 2026-10-08 (UTC) |
| Batch id | ${BATCH} |
| Environment | STAGING (proven via /api/operational/build-info: environment=Staging, notifications disabled, in-memory media off, redis/storage isolation markers contain "staging", 67/67 migrations applied) |
| Commit SHA (API deployed) | ${st.gate.commitSha} |
| API version | ${st.gate.version} |
| Frontend | https://staging--hudhudnest.netlify.app (version not exposed to a non-admin; STAGING badge visible) |
| Database | Render Postgres, isolation marker ${st.gate.isolation.databaseMarker} (not directly queried: no DB credentials were used) |
| Accounts created | ${st.accounts.length} (100 owners, 1 probe, 2 repro) |
| Listings created | ${st.listings.length} via the real API (+1 probe, +2 repro, all identified by owner) |
| Images uploaded | ${images} through the real upload endpoint (+ a few in probe/repro) |
| Cities (governorates) | 12: ${Object.entries(byGov).map(([k, v]) => `${k} ${v}`).join(', ')} |
| Property types | all 13 existing types: ${Object.entries(count('propertyType')).map(([k, v]) => `${k} ${v}`).join(', ')} |
| Listing types | ForRent ${count('listingType').ForRent}, ForSale ${count('listingType').ForSale}, ForRentAndSale ${count('listingType').ForRentAndSale}; Short stay and Investment: NOT created (see Blocked) |
| Currencies | ${Object.entries(count('currency')).map(([k, v]) => `${k} ${v}`).join(', ')} |
| Content languages | ar ${count('lang').ar}, en ${count('lang').en}, de ${count('lang').de} |
| Tests executed | ${total} (API ${api.length} + browser ${ui.length}) |
| Passed / Failed / Blocked | ${passed} / ${failed} / ${blocked} |
| Issues | P0 ${sev.P0}, P1 ${sev.P1}, P2 ${sev.P2}, P3 ${sev.P3} |

Production was never contacted. All data lives in Staging; image files are stored under the \`staging/\` Cloudinary folder.

## Safety gate (Phase 0)

STAGING SAFETY CHECK: PASS. Every script re-runs the gate (build-info with the Staging automation secret) before any write and aborts on any mismatch.

## What was done

1. Registered ${st.accounts.length} synthetic accounts through the real phone-OTP flow (reserved test phone prefix, fixed Staging OTP), 0 failures.
2. Created ${st.listings.length} listings (one per owner) with deterministic, varied data and uploaded ${images} generated PNG images (0 / 1 / 3 / 4 / 6 / 10 per listing; no real people).
3. Verified create -> persist -> retrieve -> search -> filter -> display -> interact -> authorize -> measure over the API and in the browser (desktop 1280, tablet 768, mobile 375; AR/EN/DE).
4. Clean-room reproductions for the image defects.

## Test Summary Matrix

| Area | Tests | Passed | Failed | Blocked | Severity |
|---|---:|---:|---:|---:|---|
${matrix}

Failed tests:
${fails}

Blocked tests:
${blockedList}

Additionally blocked / not covered (see Limitations): listing statuses Draft/Pending/Rejected do not exist; direct database integrity queries; direct multi-listing-per-account distribution (free-plan quota).

## Issues

${bugMd}

## Observations (not defects, or needing a decision)

${obs.map((o) => `- ${o}`).join('\n')}

## Performance (Staging, single client from one location, Render free tier)

| Scenario | Requests | Avg ms | P95 ms | P99 ms | HTTP status counts | 5xx |
|---|---:|---:|---:|---:|---|---:|
${perfTbl}

All requests observed during the whole run, grouped by endpoint (includes correlated test traffic and expected 4xx):

| Endpoint | Requests | Avg ms | P95 ms | P99 ms | HTTP >= 400 |
|---|---:|---:|---:|---:|---:|
${apiTbl}

Image CDN (HEAD, ${st.imagePerf ? Math.round(st.imagePerf.n) : '-'} requests): avg ${st.imagePerf ? Math.round(st.imagePerf.avg) : '-'} ms, P95 ${st.imagePerf ? Math.round(st.imagePerf.p95) : '-'} ms, P99 ${st.imagePerf ? Math.round(st.imagePerf.p99) : '-'} ms; all 411 batch URLs returned 200 image/*.
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

NOT performed. Data is intentionally retained for manual inspection. Everything created by this test is identifiable by owner (last name contains ${BATCH}) and by the IDs in STAGING-TEST-DATA.md. Run \`node staging-qa/cleanup.mjs\` (after restoring the Staging secrets file) to delete only these accounts' listings; it verifies each account's last name contains the batch id before deleting. Pre-existing Staging listings (2) were not touched.

## Answers to the required questions

1. Can accounts be created? YES (${st.accounts.length}/${st.accounts.length}).
2. Can users log in? YES; wrong password and unknown phone behave identically (no enumeration); logout revokes refresh.
3. Can listings be created? YES, 100/100; quota (free = 1) enforced with 409.
4. Can images be uploaded? YES, ${images}/${images}; validation rejects fake/svg/exe/11 files. BUT see STG-001/002 for deletion.
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

\`\`\`text
FAIL
\`\`\`

Rationale: the core journey (register -> login -> create -> upload -> publish -> search -> filter -> paginate -> view -> favorite -> contact -> authorize) works on 100 listings with no security defect, but two reproducible P1 defects break owner image management (STG-001, STG-002) and several acceptance items remain BLOCKED. Fix the two P1 items and re-run \`staging-qa/repro-main-image.mjs\` and \`staging-qa/tests.mjs\` before promoting to Production.
`;
writeFileSync(OUT + 'STAGING-TEST-REPORT.md', report);

// ---- data file ----
const idRows = st.listings.map((l) => `| ${l.index} | ${l.id} | ${l.ownerId} | ${GOVN[l.governorateId]} | ${l.propertyType} | ${l.listingType} | ${l.currency} | ${l.lang} | ${l.images.length} |`).join('\n');
const data = `# STAGING-TEST-DATA

| Item | Value |
|---|---|
| Test batch id | ${BATCH} (carried in account last names and in each listing description suffix; the schema has no batch column) |
| Environment | Staging (API ${st.gate.commitSha.slice(0, 7)}) |
| Accounts | ${st.accounts.length}: 100 owners (index 001-100), 1 probe (index 900), 2 repro (901, 902) |
| Listings | ${st.listings.length} batch listings (one per owner) + probe listing + 2 repro listings; 2 pre-existing Staging listings untouched |
| Images | ${images} in batch listings; image-count distribution ${JSON.stringify(count('wantedImages'))} |
| Cities | ${JSON.stringify(byGov)} |
| Property types | ${JSON.stringify(count('propertyType'))} |
| Listing types | ${JSON.stringify(count('listingType'))} |
| Currencies | ${JSON.stringify(count('currency'))} |
| Languages of content | ${JSON.stringify(count('lang'))} |
| Configuration | Free plan per owner; fixed Staging OTP mechanism; password and secrets NOT recorded |
| Cleanup status | NOT performed (data retained for manual inspection) |
| Notable test objects | probe listing 5f282dc5-8285-4174-b1d6-70f7fdc340ae (stuck without upload ability, STG-002); repro listings ${JSON.stringify(st.reproListings)} |

## Created listing IDs

| # | Listing id | Owner user id | City | Type | Listing type | Currency | Language | Images |
|---:|---|---|---|---|---|---|---|---:|
${idRows}
`;
writeFileSync(OUT + 'STAGING-TEST-DATA.md', data);

// ---- machine-readable ----
const json = {
  environment: 'staging', batchId: BATCH, commitSha: st.gate.commitSha, testedAt: st.testsRanAt,
  accountsCreated: st.accounts.length, listingsCreated: st.listings.length, imagesUploaded: images,
  testsExecuted: total, passed, failed, blocked,
  verdict: 'FAIL',
  issues: bugs.map((b) => ({ id: b.id, severity: b.sev, area: b.area, title: b.title })),
  results: rows.map(({ area, id, name, status, detail }) => ({ area, id, name, status, detail })),
  performance: st.perf ?? [], cleanup: 'not performed',
};
writeFileSync(OUT + 'staging-test-results.json', JSON.stringify(json, null, 1));

// ---- evidence ----
mkdirSync(OUT + 'staging-test-evidence', { recursive: true });
for (const f of ['tests.log', 'results.json', 'repro-main-image.json']) copyFileSync(f, OUT + 'staging-test-evidence/' + f);
console.log({ total, passed, failed, blocked, sev });
