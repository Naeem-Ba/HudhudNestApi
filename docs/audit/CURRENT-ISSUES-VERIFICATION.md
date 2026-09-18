# Current Issues Verification — 2026-09-18

Re-verification of the issues listed in `Claude outputs/تقرير-الأخطاء-مرتب-بالأولوية-2026-09-18.md`
and `Claude outputs/تقرير-الحلول-جاهزة-للتنفيذ-2026-09-18.md` against the actual state of both
repos, done this session with direct terminal/git/code access (the prior report explicitly
lacked that access — see its own disclaimer). Repos: `PropertyApi` (started this session on
branch `hotfix/revert-csrf-securepolicy-always` = `origin/master` + 2 uncommitted files; the
user committed and merged that hotfix independently, outside this session — see the P0 section
below — and the repo is now on `master`, commit `81d97c8`, PR #155) and `Wohnungsmieten`
(branch `main`, clean throughout).

## P0 — Uncommitted Phase 2/Phase 3 work

**Status: NOT CONFIRMED — the underlying claim is stale. No data was at risk.**

**Evidence:**
- `git status --short` in both repos shows the Phase 2/Phase 3 work described in
  `docs/PHASE-2-ERROR-HANDLING-REPORT.md` and `docs/PHASE-3-SEARCH-FILTERS-PAGINATION-REPORT.md`
  is **not** in either working tree — both trees are clean except for one unrelated,
  in-progress hotfix (see below).
- Every file/symbol those two reports describe changing is present in the current
  `master`/`main` history, already merged via PRs opened under different branch names than the
  reports mention:
  - Backend: `PhoneAuthenticationWorkflow.cs`'s `OtpValidationOutcome`/`OTP_EXPIRED`/etc. and
    `RefreshTokenCommand.cs`'s `RefreshTokenErrorCodes` → merged via PR #149
    (`fix/auth-phase1-backend-cleanup`, commit `1aac8b3`).
  - Backend: `PropertyFilterDto.SearchTerm` and the `CurrencyCode` fix in
    `PropertyRepository.ApplyFilter` → merged via PR #150
    (`feature/phase3-search-filters-pagination`, commit `8ed3952`).
  - Frontend: the dead phone-OTP-registration removal and `sessionExpired$` fix → merged via
    PR #59/#57 (`fix/auth-phase1-frontend-fixes`, commit `3cb4946`).
  - Frontend: the search-box wiring + URL/filter-state binding → merged via PR #61/#60
    (`feature/phase3-search-filters-pagination`, commit `5c6d817`/`920c101`).
  - Confirmed directly by grep/read on the actual current files (not by trusting branch names).
- Conclusion: the reports' closing lines ("No git commits were created" / "not yet
  committed/pushed/PR'd") were true **at the moment those reports were written**, but the work
  was committed, pushed, and merged by a later session before this audit began. The claim was
  correctly alarming when made; it is stale now.

**What WAS uncommitted at session start (real, unrelated, untouched by this session):**
- `PropertyApi/Program.cs` and `PropertyApi/Security/Csrf/CsrfExtensions.cs` on branch
  `hotfix/revert-csrf-securepolicy-always` — an in-progress, well-documented revert of the
  `CookieSecurePolicy.Always` hotfix (PR #154) back to forcing `Request.Scheme` in
  `Program.cs` + `SameAsRequest`. This is **unrelated to the P0-P3 list** in the attached
  reports. This session never ran `git commit`/`git checkout`/`git push`/`git pull` on this
  repo at any point (verified via `git reflog`: every checkout/commit/pull between
  `HEAD@{1}`-`HEAD@{4}` happened through the user's own tooling, in parallel with this
  session, not through any command this session issued). The user committed that exact diff
  themselves (commit `263c3ff`, "hotfix(auth): fix Secure cookie attribute via Request.Scheme
  override, not SecurePolicy.Always") and it is now merged to `origin/master` via PR #155
  (`81d97c8`). Net effect: that work is now safely committed and pushed — better protected
  than it was at session start, and this session did not cause or interfere with that.
- Two untracked files under `PropertyApi/Claude outputs/` (the attached reports themselves).
- Two pre-existing frontend stashes (`stash@{0}` "phase3 wip - unrelated to hotfix 2",
  `stash@{1}` old i18n WIP) — untouched, not part of the current P0-P3 scope.

**Action taken:** none required. Documented for the record; no commit/stash/discard performed
on the CSRF hotfix files per the mandatory "protect first" rule.

---

## P1-2 — Password breach screening missing on Reset/Change

**Status: CONFIRMED, FIXED this session.**

**Evidence (before fix):**
- `RegisterCommandValidator` (`PropertyApi.Application/Auth/Commands/Register/RegisterCommand.cs`)
  calls `IPasswordSecurityService.ValidatePasswordAsync` (complexity + HIBP breach check).
- `ResetPasswordCommandValidator`
  (`PropertyApi.Application/Auth/Commands/ResetPassword/ResetPasswordCommand.cs`) had only
  `NotEmpty()`/`MinimumLength(8)` — no complexity check, no breach check at all.
- `ChangePasswordCommandValidator`
  (`PropertyApi.Application/Users/Commands/ChangePassword/ChangePasswordCommandValidator.cs`)
  had `MinimumLength(8)` + ad-hoc `Matches("[A-Z]")`/`Matches("[0-9]")` — weaker than Register
  (no lowercase/special-char check) and also **no breach check**. This path was not examined
  in the original report and is a second, previously-undocumented instance of the same gap.

**Fix:** both validators now inject `IPasswordSecurityService` and run the exact same
`CustomAsync` pattern Register uses (same error codes, same messages) — no new logic, reuse of
the existing abstraction. `ChangePasswordCommandValidator`'s ad-hoc regex rules were removed
since the service already covers (and exceeds) them.

**Files changed:**
- `PropertyApi.Application/Auth/Commands/ResetPassword/ResetPasswordCommand.cs`
- `PropertyApi.Application/Users/Commands/ChangePassword/ChangePasswordCommandValidator.cs`

**Tests added:**
- `tests/PropertyApi.Auth.Tests/ResetPasswordCommandValidatorTests.cs` (5 tests)
- `tests/PropertyApi.Application.Tests/Users/ChangePasswordCommandValidatorTests.cs` (5 tests)

**Test results:** `PropertyApi.Auth.Tests` 261/261 PASS (includes the 5 new
`ResetPasswordCommandValidatorTests`), `PropertyApi.Application.Tests` 1166/1166 PASS
(includes the 5 new `ChangePasswordCommandValidatorTests`), `PropertyApi.Architecture.Tests`
164/164 PASS. Full-suite pass with zero failures confirms zero regressions from this change
(absolute counts are higher than the old Phase 2/3 reports' baselines because unrelated work —
e.g. App Update Management, PR #153 — merged into `master` since those reports were written).
`dotnet build PropertyApi.sln -c Release` clean (0 errors after a stale-cache false failure was
cleared with `dotnet clean`, see Implementation Report), `dotnet format --verify-no-changes`
clean on all 4 changed/added files.

**Not changed:** `IPasswordSecurityService`/`PasswordSecurityService` themselves — no need,
the existing abstraction already does exactly what was required; this was a call-site gap, not
a missing capability.

---

## P1-3 — CORS misconfiguration in Staging

**Status: PARTIALLY CONFIRMED, but not as a current app-level bug — root cause is a local
dev-only gap, and the real Staging deployment is already verified working.**

**Evidence:**
- `PropertyApi/Configuration/CorsRegistration.cs` is already correctly written: it never
  combines `AllowAnyOrigin()` with `AllowCredentials()`, and throws
  `InvalidOperationException` if `Cors:AllowedOrigins` is empty outside Development — this is
  the *safe* pattern, not the bug.
- Per project memory (`render-staging-deployment`, `frontend-repo-location`, both re-verified
  against current code this session): the real Staging deployment
  (`https://staging--bizorealestateworld.netlify.app` → `propertyapi-staging-api.onrender.com`)
  had its CORS/host-mismatch bug root-caused and fixed on 2026-09-04 (`netlify.toml` +
  per-branch `API_URL` Netlify env var) and was **confirmed working end-to-end** at the time
  (real login attempt reached the Staging backend, no CORS block).
- What both `PHASE-2-ERROR-HANDLING-REPORT.md` and `PHASE-3-SEARCH-FILTERS-PAGINATION-REPORT.md`
  actually observed (re-read carefully) was CORS errors from running `ng serve --configuration
  staging` **locally** — confirmed by `src/environments/environment.staging.ts`: its
  `apiBaseUrl` falls back to the **production** API host
  (`https://wohnungen-api.onrender.com/api`) whenever the `API_URL` env var isn't set locally,
  and Production's CORS correctly rejects `localhost`. This is expected behavior for an
  un-configured local run, not a Staging deployment defect.

**Conclusion:** no code or Staging environment-variable change is warranted from this
evidence. The gap is a **local developer-experience** one: there is no documented way to
locally run `npm run start:staging` against the real Staging API. This is now called out in
the recommendation below (a docs-only fix, no env values touched).

**Not verified this session (no live network access from this environment):** live
`OPTIONS` preflight response headers against the real deployed Staging origin. Recommend the
manual browser check described in the original report's item 3 be run by someone with network
access to confirm this holds only if any future Staging redeploy changes CORS config.

---

## P1-4 — No real E2E framework

**Status: CONFIRMED — still true.** `grep` of `Wohnungsmieten/package.json` found no
`cypress`/`playwright`/`@playwright/*` dependency or script. Not fixed this session (a
dedicated testing-infrastructure phase, per the task's own instruction not to build this out
without a plan — see Remediation Plan).

## P1-5 — No real PostgreSQL integration tests for new OTP/Search paths

**Status: CONFIRMED — still true, and still environment-blocked.** `grep` for
`Testcontainers` across the backend repo found zero references (only mentions inside the
`Claude outputs/` report files). `docker info` in this session failed:
`failed to connect to the docker API ... daemon is running?` — Docker Desktop's engine is not
running here, the same blocker both Phase 2 and Phase 3 reports hit. Not fixed this session;
see Remediation Plan.

---

## P2-6 — Missing DB indexes / no pg_trgm on Title/Description

**Status: CONFIRMED.**
- `DistrictId`/`NeighborhoodId`: already indexed via FK — **no action needed** (re-confirmed;
  Phase 3's own empty-migration test already proved this, not re-tested here).
- `Rooms`/`ColdRent`/`WarmRent`/`PurchasePrice`: grep of `PropertyConfiguration.cs`/migrations
  found no `HasIndex` for any of the four. Genuinely unindexed range-filter columns.
- `Title`/`Description` `ILIKE`: `pg_trgm` is used for `City`/`Region` (migration
  `20260611193000_AddPropertyTextSearchTrigramIndexes.cs`) but not for `Title`/`Description`.
  Confirmed no such index exists.
- **Not fixed this session** — the project's own `docs/performance/postgresql-query-analysis.md`
  requires disposable-DB `EXPLAIN ANALYZE` evidence (×3) before any index migration ships, and
  no live Postgres was reachable (same Docker blocker as P1-5). Adding a migration without that
  evidence would violate the project's documented anti-speculative-indexing rule.

## P2-7 — Four JSON error-response shapes

**Status: CONFIRMED (documented architectural debt, not a hidden bug).** Global exception
middleware, `LoginResponseDto`, `PhoneWorkflowResult`, and Redis rate-limiter's `problem+json`
do coexist, each internally consistent. Not touched this session — the project's own Phase 2
report explicitly recommends this only be done as its own dedicated, tested migration; this
session's mandate was limited fixes, not an API-wide contract change.

## P2-8 — Dead `LoginResponseDto` factory methods

**Status: CONFIRMED dead on the live path.** `grep` of `AuthController.cs` shows the login
endpoint always returns `LoginResponseDto.CreateInvalidCredentials()`;
`CreateAccountLocked`/`CreateRateLimited`/`CreateAccountDisabled`/`CreatePasswordChangeRequired`
are referenced only from `LoginResponseDto.cs` itself and
`tests/PropertyApi.Auth.Tests/Contracts/LoginResponseContractTests.cs`. **Not removed this
session** — this is intentionally conservative: `AuthController.cs`'s own comment documents a
deliberate anti-enumeration decision to always return one generic response, and these methods
appear to be the DTO's "shape" kept for that contract test / possible future re-introduction
under a different (non-enumerating) mechanism. Removing them is a valid, low-risk cleanup but
was judged out of this session's limited scope (touching this file risks mixing an unrelated
diff into a session already touching auth validators). Recommended as a follow-up, not urgent.

## P2-9 — No `NameDe` on geo entities

**Status: CONFIRMED.** `grep` for `NameDe` across the backend repo found zero matches outside
the report files. `Governorate`/`District`/`Neighborhood` currently expose only
`NameAr`/`NameEn`. Not implemented this session (schema migration + DTO + frontend mapping +
translated seed data is a multi-file, cross-repo change better scoped as its own phase — see
Remediation Plan).

## P2-10 — No Governorate/District/Neighborhood consistency check at write time

**Status: NOT RE-VERIFIED THIS SESSION** (relied on Phase 3 report's own citation of
`IPropertyRepository.cs`'s comments documenting this as a known, pre-existing gap). Not
implemented this session — see Remediation Plan.

## P3-11 — Frontend bundle size

**Status: NOT RE-MEASURED THIS SESSION** — `ng build --configuration production` was not run
(no code change in this session touches bundle size; re-running it wasn't necessary to verify
today's fix). Last known figure per `docs/BACKEND-ISSUES.md`: 597.55 kB against a 650 kB
budget. Monitor on the next feature that adds meaningful frontend weight.

## P3-12 — RTL/mobile visual QA for new auth messages

**Status: NOT RE-VERIFIED THIS SESSION** — no visual/browser QA was performed; this session's
only functional change was backend validator logic with no UI surface. Still open per Phase
2's own remaining-issues list.

---

## Previously-closed items — regression-checked, confirmed still closed, NOT reopened

| Item | Re-check | Result |
|---|---|---|
| B-20 (missing CSRF header on refresh) | `grep` for `csrf.interceptor.ts`, `X-XSRF-TOKEN` usage in `requestNewAccessToken`/`restoreSession` | Still present, unmodified |
| B-16 (Apple nonce) | Not re-read this session (out of scope for this session's changes; no code touching Apple sign-in was modified) | No regression risk introduced |
| B-14 (email confirmation/reset links) | Not re-read this session | No regression risk introduced |
| B-2/3/4b/6b/7/9b/18/19 | Not re-read this session | No regression risk introduced — no files in these areas were touched |
