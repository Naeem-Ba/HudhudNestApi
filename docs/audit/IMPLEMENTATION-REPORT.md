# Implementation Report — 2026-09-18

## What changed

| File | Change |
|---|---|
| `PropertyApi.Application/Auth/Commands/ResetPassword/ResetPasswordCommand.cs` | `ResetPasswordCommandValidator` now injects `IPasswordSecurityService` and runs the same breach/complexity check `RegisterCommandValidator` uses (`CustomAsync(ValidatePasswordSecurityAsync)`), instead of only `MinimumLength(8)`. |
| `PropertyApi.Application/Users/Commands/ChangePassword/ChangePasswordCommandValidator.cs` | Same fix; also removes the previous ad-hoc `Matches("[A-Z]")`/`Matches("[0-9]")` rules, which were weaker than and inconsistent with Register/Reset (no lowercase/special-char/breach check). |
| `tests/PropertyApi.Auth.Tests/ResetPasswordCommandValidatorTests.cs` (new) | 5 tests: breached password rejected, stable error code carried through, weak-complexity rejected, valid password passes, confirm-password mismatch still enforced. |
| `tests/PropertyApi.Application.Tests/Users/ChangePasswordCommandValidatorTests.cs` (new) | 5 tests: breached password rejected, stable error code carried through, no-lowercase now rejected (regression guard for what the old regex rule missed), valid password passes, empty `UserId` still rejected. |

No other files were modified. No `docs/*` file other than the three new `docs/audit/*.md`
files (this report, the verification report, the remediation plan) was changed.

## Why each change was made

`RegisterCommandValidator` already delegates all password-strength/breach logic to
`IPasswordSecurityService.ValidatePasswordAsync` — a single abstraction that checks length,
character classes, common-word/long-run patterns, and Have I Been Pwned breach status, and
fails open (never blocks on an HIBP outage). `ResetPasswordCommandValidator` and
`ChangePasswordCommandValidator` did not call it at all, so a user could set exactly the
password Registration would have refused via either Reset or Change — a real, if
self-inflicted, security policy inconsistency. The fix reuses the existing abstraction
verbatim (same `CustomAsync` shape, same error-code propagation) rather than writing new
validation logic, per the task's explicit instruction to unify the policy through the
existing abstraction, not duplicate it.

## Tests run and results

| Command | Result |
|---|---|
| `dotnet build PropertyApi.sln -c Release` | Initial attempt failed with `CS0234`/`CS0246` on `AppUpdates`/`AppRelease` types that verifiably exist and are committed (confirmed via `git status`, file reads). Root-caused as a stale incremental-build cache (this was the first build in this checkout this session) — `dotnet clean PropertyApi.sln -c Release` followed by a fresh `dotnet build` succeeded with **0 errors, 0 warnings**. Not caused by, or related to, this session's changes. |
| `dotnet test tests/PropertyApi.Auth.Tests -c Release --no-build` | **261/261 PASS** (includes the 5 new Reset validator tests) |
| `dotnet test tests/PropertyApi.Application.Tests -c Release --no-build` | **1166/1166 PASS** (includes the 5 new ChangePassword validator tests) |
| `dotnet test tests/PropertyApi.Architecture.Tests -c Release --no-build` | **164/164 PASS** — confirms no architectural-convention regression (anonymous-endpoint/rate-limit invariants etc.) |
| `dotnet format PropertyApi.sln --verify-no-changes --include <4 changed/added files>` | Clean, no formatting diffs |

**Not run this session** (no code path touches them, so re-running added no verification
value for *this* change): `PropertyApi.Integration.Tests`, `PropertyApi.Concurrency.Tests`,
`PropertyApi.Performance.Tests`, `PropertyApi.StagingSmokeTests`, `PropertyApi.Observability.Tests`,
and the entire `Wohnungsmieten` frontend suite (`typecheck`, `ng test`, `ng build`) — this
session made zero frontend changes.

## Documentation updated

- `docs/audit/CURRENT-ISSUES-VERIFICATION.md` (new) — per-item verified/unverified status with
  direct evidence for every P0-P3 item in the attached reports.
- `docs/audit/REMEDIATION-PLAN.md` (new) — phased plan for everything still open.
- `docs/audit/IMPLEMENTATION-REPORT.md` (this file, new).

`docs/PHASE-2-ERROR-HANDLING-REPORT.md` and `docs/PHASE-3-SEARCH-FILTERS-PAGINATION-REPORT.md`
were **not edited** — their content is historically accurate for when they were written; their
"not committed" closing lines are now known-stale (see verification doc) but rewriting
someone else's dated session report after the fact was judged out of scope; the verification
doc is the correct place to record the correction instead.

## What was NOT implemented (see Remediation Plan for detail)

- P1-3 (CORS): no code change needed — confirmed as a local-dev-only documentation gap, not
  an app or Staging-deployment bug. A doc fix is recommended but was not written this session
  (kept the change set to the one verified, tested fix — see note below).
- P1-4 (E2E framework): not started — genuinely out of a single safe session's scope per the
  task's own instruction; phased plan written instead.
- P1-5 (Postgres integration tests): blocked — Docker Desktop's engine is not running in this
  environment (`docker info` fails); cannot be executed here regardless of session scope.
- P2-6 (indexes): blocked on the same Docker/Postgres access; the project's own
  performance-analysis procedure forbids shipping a speculative index migration without
  `EXPLAIN ANALYZE` evidence, which requires that same access.
- P2-7 (unify 4 error shapes), P2-8 (dead `LoginResponseDto` factories), P2-9 (`NameDe`),
  P2-10 (geo write-time consistency): all confirmed open, none implemented — each requires
  either a dedicated multi-file phase (P2-7, P2-9, P2-10) or is optional low-priority cleanup
  best folded into an unrelated future PR (P2-8), per the task's instruction not to expand
  scope beyond what today's evidence justified fixing now.
- P3-11 (bundle size), P3-12 (RTL/mobile visual QA): not re-measured/re-tested — no UI change
  was made this session that could affect either.

## Remaining risks

- The password-breach fix depends on `PasswordSecurityService`'s existing fail-open behavior
  on an HIBP outage — unchanged by this session, but worth knowing: a prolonged HIBP outage
  means Reset/Change (like Register already did) will accept a breached password rather than
  block the user. This is the project's existing, documented trade-off, not a new one.
- `ChangePasswordCommandValidator`'s stricter rules (lowercase + special character, previously
  not enforced) mean a currently-valid-but-non-compliant password a user might try to *reuse*
  during a change flow will now be rejected where it previously wasn't — this is the intended
  fix, but is a genuine (small) behavior change for end users, not purely internal.
- The CSRF-revert hotfix that was uncommitted on `hotfix/revert-csrf-securepolicy-always` at
  session start (`Program.cs`, `CsrfExtensions.cs`) was committed, merged, and pulled into
  `master` (PR #155, commit `81d97c8`) by the user's own tooling in parallel with this
  session — confirmed via `git reflog`, not an action this session took. This session's repo
  is now on `master` at that commit; the password-validator fix and its tests were rebuilt and
  re-tested on top of it (still 261/261, 1166/1166, 164/164 green) to confirm no interaction.

## Actions NOT performed in the original pass (require explicit approval)

- No `git add`/`git commit`/`git push` was run in either repo.
- No merge, no PR creation, no deploy.
- No Staging/Production environment variable was read, set, or changed.
- No branch or worktree was deleted.

**Update:** the fix above was subsequently committed, pushed, and merged as
[PR #156](https://github.com/Naeem-Ba/PropertyApi/pull/156), on the user's explicit
instruction in a later turn (`/create-pr`). See the follow-up section below for what happened
after that.

---

## Follow-up session — 2026-09-18, later same day — remediation plan execution

Executed the remaining P1-P3 items from `docs/audit/REMEDIATION-PLAN.md` as it stood right
after the original pass (that file has since been overwritten on disk by an unrelated,
separately-run "Full Code Audit" — see the note at the top of
`CURRENT-ISSUES-VERIFICATION.md`; this section is the authoritative record of what *this*
line of work did).

### What changed

| File | Change |
|---|---|
| `PropertyApi/Program.cs` | Extended the Production-only `Request.Scheme = "https"` override (from today's earlier PR #155) to also cover Staging (`IsProduction() \|\| IsStaging()`). **Uncommitted — see below, requires explicit approval.** |
| `Wohnungsmieten/playwright.config.ts` (new) | Playwright config; targets deployed Staging by default, `E2E_BASE_URL` override for local runs. |
| `Wohnungsmieten/e2e/auth-session-refresh.spec.ts` (new) | Phase A E2E spec (P1-4) — session-refresh-after-expired-token scenario. |
| `Wohnungsmieten/package.json`, `package-lock.json` | Added `@playwright/test` devDependency. |
| `Wohnungsmieten/.gitignore` | Added Playwright output directories (`test-results/`, `playwright-report/`, etc.). |
| `Wohnungsmieten/docs/development/local-staging-testing.md` (new) | P1-3 — documents the `API_URL` env var needed to run `ng serve --configuration staging` locally against the real Staging API, and the separate, confirmed CORS constraint that still blocks it from `localhost` specifically. |

### Why

- **P1-3 (CORS):** confirmed via a live `curl -X OPTIONS` against the real Staging API that
  (a) the deployed Netlify Staging origin is correctly allowed (204, matching origin), and
  (b) `localhost` is not, and never was expected to be — this is a local dev-experience gap,
  documented, not a Staging misconfiguration. No code or environment change made.
- **P1-4 (E2E):** implemented Phase A exactly as scoped in the remediation plan — one
  scenario, not a full suite, not wired into a CI gate. Actually running it against live
  Staging (this session unexpectedly had network egress) surfaced two real findings instead of
  a theoretical "no framework exists" gap:
  1. Deployed Staging's frontend bundle is behind current `main` (missing the consent
     checkbox) — a deploy-staleness observation, not something this session's scope covers
     fixing (would require triggering a Staging deploy).
  2. `/auth/refresh` returns `403` in Staging because the Production-only HTTPS-scheme fix
     from PR #155 was never extended to Staging, which has the identical root cause. Fixed in
     code (see `Program.cs` above) but **not deployed** — this touches the single most
     historically incident-prone area of this codebase (CSRF/cookie config, 5 touches now),
     so it is held for explicit approval rather than pushed automatically, per this task's own
     standing rule on Staging/Production-affecting changes.

### Tests run and results

| Command | Result |
|---|---|
| `dotnet build PropertyApi.sln -c Release` | 0 errors, 0 warnings (after the `Program.cs` change) |
| `dotnet test tests/PropertyApi.Architecture.Tests --no-build` | 164/164 PASS |
| `dotnet test tests/PropertyApi.Auth.Tests --no-build` | 261/261 PASS |
| `dotnet format --verify-no-changes --include PropertyApi/Program.cs` | clean |
| `npx playwright test` (E2E spec), run 1, against deployed Staging frontend | FAILED — registration step, deploy-staleness (see above); not a code defect |
| `npx playwright test`, run 2, against a local `ng serve --configuration staging` pointed at the real Staging API | FAILED — CORS-blocked from `localhost` (confirmed expected, see P1-3) |
| `npx playwright test`, run 3, using a directly API-registered throwaway account against the deployed Staging frontend | FAILED at the `/auth/refresh` assertion — **this is the real bug** (see above), not a test defect; login itself, the forced-401 simulation, and the interceptor's reactive refresh attempt all worked exactly as designed up to that point |

The E2E spec was not re-run after the `Program.cs` fix because that fix is not deployed
anywhere the spec could observe it (it only exists in this local working tree) — re-running
against unchanged live Staging would just reproduce the same 403. It should be re-run once the
fix is deployed, as the acceptance check for that deploy.

### What was NOT implemented this follow-up pass

- P1-5 / P2-6 (Testcontainers, DB indexes): still blocked — `docker info` still fails in this
  environment (re-checked at the start of this pass).
- P2-7 (unify 4 error shapes), P2-9 (`NameDe`), P2-10 (geo write-time consistency): not
  started this pass — ran out of session scope after the E2E work surfaced the Staging CSRF
  finding, which took priority to investigate and document properly rather than leaving it as
  an unexplained red test.
- P2-8, P3-11, P3-12: unchanged from the original pass — still open, still low-priority.

### Remaining risks

- The `Program.cs` Staging fix is **reasoned and unit/architecture-test-clean but not deployed
  or live-verified** — the only way to fully verify it is to deploy it to Staging and re-run
  the E2E spec, which needs explicit approval.
- Until that fix deploys, `/auth/refresh` — and by extension any CSRF-guarded endpoint reached
  while `refresh_token` is present — is presumed broken in Staging for real browser clients.
  This may already be masking other live-Staging test failures beyond what this session probed.
- One throwaway Staging test account was created (see `CURRENT-ISSUES-VERIFICATION.md`,
  P1-4) — no cleanup secret was available or used; it will age out under the existing
  `E2E-SMOKE-` TTL policy.

### Actions NOT performed this pass (require explicit approval)

- `PropertyApi/Program.cs`'s Staging fix was **not** committed, pushed, or deployed.
- The new Playwright files (config, spec, `package.json`/`package-lock.json`,
  `.gitignore`, docs) were **not** committed or pushed.
- No Staging environment variable or deployment was triggered.

---

## Security/quality audit remediation — 2026-09-19

Each finding was re-verified against the live code first; several audit claims were stale or
only partly true (noted below).

| Finding | Verified state | Action |
|---|---|---|
| Android signing password in shared archive | File never in git history (`.gitignore` correct since PR #37); exposure was via a plain zip, which ignores `.gitignore`. Owner confirmed an archive was shared. | Store password rotated with `keytool -storepasswd` (same key: identical SHA-256 fingerprint before/after). `RELEASE_STORE_PASSWORD`/`RELEASE_KEY_PASSWORD` set in GitHub Secrets. Details in the frontend repo's `android/RELEASE_SIGNING.md`. PKCS12 has no separate key password (`-keypasswd` unsupported), so both stay identical. |
| Staging falls back to Production API | Real: `environment.staging.ts` defaulted to `propertyapi-api.onrender.com`. `netlify.toml` has no `[[redirects]]`, so the Netlify half of the claim was already stale. | Staging fallback removed; the bundle now throws at load if `API_URL` is missing. Production keeps its own fallback (intended). |
| Short-Stay edit wipes data | Real, frontend-side: the form hardcoded `governorateId/districtId/neighborhoodId/pool*` to `null` and forced `depositPercentage = null` on load; the PUT is full-replace, so `null` is an explicit clear. The GET DTO also never returned `DepositPercentage`/location ids. | `ShortStayListingDto` now returns them; the form captures them on load and echoes them back on save. Backend full-replace semantics deliberately unchanged. |
| `npm ci` fails (no lockfile) | **Not reproducible**: `package-lock.json` is tracked, not ignored, matches HEAD; `npm ci` succeeds from a clean `node_modules`. The one failure seen was a local OneDrive file lock (EPERM). | None needed. |
| Unbounded `pageSize` | Real (floor of 1, no ceiling, anonymous endpoint). | `SearchShortStayListingsQueryValidator` (1–100) plus `Math.Clamp` in the repository. |
| Missing validators / `Enum.Parse` → 500 | Real for `UpdateShortStayListingCommand` and `SetPricingRulesCommand`. | New validators using `Enum.TryParse` guards, numeric bounds, deposit 0–100. |
| `PropertyId` without ownership check | Real. | `CreateShortStayListingCommandHandler` now calls `IPropertyOwnershipService.EnsureOwnerAsync`. |
| Non-atomic multi-request save | Real but mitigated by design: `IsPublished` defaults to false and public reads filter on it, so a partial sequence leaves an invisible draft, not corrupt live data. | Documented; no saga/transaction redesign. |
| GitHub Actions not SHA-pinned (frontend) | Real, 9/9. | All pinned to commit SHAs with version comments. |
| Availability unbounded range | Real (also on booking creation). | 366-day cap in `GetUnitAvailabilityQueryValidator` and `CreateBookingCommandValidator`. |
| Archive hygiene (`.claude/worktrees`, Gradle cache) | Process issue with how the zip was made, not a repo defect. | Use `git archive HEAD` (respects `.gitignore`/tracked files only) for any hand-off; never zip the working directory. |
| Frontend telemetry limited | Not addressed; product/observability scope. | Deferred. |

Tests: Application.Tests 1186/1186 (+20 new: validators and ownership), Architecture 164/164,
Auth 261/261, ShortStay integration 4/4; frontend typecheck clean, unit 323/323.
