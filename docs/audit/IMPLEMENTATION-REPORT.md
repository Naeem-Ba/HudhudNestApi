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

## Actions NOT performed (require explicit approval)

- No `git add`/`git commit`/`git push` was run in either repo.
- No merge, no PR creation, no deploy.
- No Staging/Production environment variable was read, set, or changed.
- No branch or worktree was deleted.
