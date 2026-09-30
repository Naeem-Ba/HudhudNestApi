# CI/CD Audit Report — HudhudNestApi

Companion detail report to `FULL-CODE-AUDIT-REPORT.md`. Covers all 9 GitHub Actions workflows and the full test suite. Read-only audit; nothing was triggered, merged, or deployed.

## Part A — Workflows

### A1. Trigger / job map (confirmed by direct reading)

| Workflow | Trigger | Jobs |
|---|---|---|
| `ci.yml` | push (main/master/develop/codex/feature/fix branches), PR, dispatch | `build-and-test`, `secret-scan` (Gitleaks), `validate-repo-hygiene` — independent, no `needs:` |
| `supply-chain-validation.yml` | push/PR (same pattern), dispatch | single job — package governance, Actions pinning, vuln scan |
| `observability-validation.yml` | PR (path-filtered), dispatch | single job |
| `performance-validation.yml` | PR/push/schedule/dispatch/`workflow_call` | single job, PR vs release/staging profile |
| `database-backup.yml` | hourly cron, dispatch | single job, fails closed if secrets missing |
| `database-restore-drill.yml` | weekly cron, dispatch, `workflow_call` (from production-gate) | single job, emits `backup_id`/`ef_core_migration` outputs |
| `redis-ha-failover.yml` | dispatch only | single job, manual drill |
| `rollback-production.yml` | dispatch only, requires 4 inputs incl. `confirm_autodeploy_disabled` | single job, hard-fails without confirmation |
| `production-gate.yml` | PR/push (main/master/develop), dispatch | 10 jobs chained via `needs:` |

`needs:` chains are real: `staging-smoke` needs `build-test-container` + `observability-validation` + `validate-redis-ha-topology` + `redis-sentinel-ha`; `deploy-production` needs 7 upstream jobs **and** re-checks each `.result == 'success'` in its own `if:` condition (belt-and-suspenders, not redundant, since `needs` alone doesn't stop a job whose `if:` doesn't reference it). **Confirmed: zero `continue-on-error: true` anywhere across all 9 workflows.**

### A2. Action pinning (Informational — corrected as part of this audit)

Every external `uses:` in all 9 workflows is pinned to a full 40-char SHA with a version comment (47 matches, all SHA-pinned, confirmed by grep). `scripts/validate-github-actions-pinning.py` enforces this format in `ci.yml`, `production-gate.yml`, and `supply-chain-validation.yml`.

`docs/security/github-actions-pinning.md`'s example table was stale (listed older SHAs for `checkout@v5`, `setup-dotnet@v5`, `upload-artifact@v6` that no workflow actually uses; the codebase consistently uses `checkout@...ba90b1 # v7.0.1`, `setup-dotnet@...06c68 # v6.0.0`, `upload-artifact@...fc6a0a # v7.0.1`) — **corrected in this audit** (see `FULL-CODE-AUDIT-REPORT.md` §9). This was documentation drift only; the enforcement script checks SHA *format*, not tag correctness, so CI itself was never affected.

### A3. `permissions:` blocks — least-privilege, confirmed

All 9 workflows declare an explicit `permissions:` block; all use `contents: read` as the base. `ci.yml` additionally grants `pull-requests: write` — no step using this permission (e.g. a `gh pr comment` call) was located in this pass. **AUD-09 (Low, needs verification):** confirm whether this is used (e.g. for a coverage-comment step not seen) or can be narrowed to `read`. `supply-chain-validation.yml` grants `pull-requests: read` only.

### A4. Secrets in logs — clean

No value-echoing found (`grep -rniE "echo.*secret"` and a broader `secrets\.` scan) — only `::error::` lines naming *which* secret is missing (safe) and `production-gate.yml`'s ephemeral-key generation step, which explicitly `::add-mask::`s each generated key before writing to `GITHUB_ENV`.

### A5. `production-gate.yml` / `rollback-production.yml` — real logic, not stubs

Both have substantive, non-placeholder implementations:

- **`production-gate.yml`**: builds a real container image, captures its immutable digest, runs Trivy (SBOM + vuln scan, JSON/SARIF output), starts the real stack via Compose, asserts non-root container user, asserts Data Protection keys persist to Postgres (not filesystem), **actively stops the Redis container and asserts liveness=200/readiness=503, then restarts and asserts recovery** — a real chaos-style dependency-failure test. `deploy-production` only fires on `workflow_dispatch` (documented reason: GitHub required-reviewers protection is unavailable on this org's billing plan, consistent with `docs/testing/staging-production-gate-checklist.md`) and requires 7 upstream job successes.
- **`rollback-production.yml`**: requires 4 manual inputs including `confirm_autodeploy_disabled`, hard-fails without it, delegates to `scripts/deployment/rollback-application.sh` (confirmed to exist) against Render's API via `RENDER_API_KEY`/`RENDER_SERVICE_ID`.

**AUD-04 (Medium, confirmed):** the `production-deployment-gate` job's "require a previous known-good Render deploy" step treats *no prior deploy found* as `previous_deploy_id="none-first-deploy"` and proceeds with only a `::warning::`, rather than blocking. This is reasonable for a genuine first deploy, but there is no way to distinguish "truly first deploy ever" from "Render API call degraded/returned empty for another reason" (wrong service ID, transient API outage) — such a case would silently be treated as first-deploy and let the release proceed without the intended prior-good-deploy check. **Fix:** add an explicit service-creation-date check, or require manual override instead of auto-proceeding on empty history.

### A6. Broken/inconsistent references — none found

All `run:` script paths and tool project paths referenced across all 9 workflows (~28 spot-checked, including `tools/HudhudNestApi.Migrator`, `tools/HudhudNestApi.DatabaseRecoveryVerifier`, compose files) exist on disk.

### A7. Other confirmed findings

- **AUD-10 (Low):** `redis-ha-failover.yml` calls `scripts/verify-redis-ha.sh` unconditionally, unlike `production-gate.yml`'s `validate-redis-ha-topology` job which has an explicit skip-if-`REDIS_HA_EVIDENCE_JSON`-unset escape hatch. Since no managed Redis HA provider is currently wired to Staging (per `docs/REDIS-HA.md`), running this workflow today would fail at that step. Low severity because it's manual-dispatch-only and doesn't block any gate — but it's presently non-functional.
- **Positive, confirmed:** `ci.yml`'s "every test project must have an owner" step (lines ~413-448) is a real structural control that already caught a historical bug (documented in-code: `HudhudNestApi.Infrastructure.Tests` was once added but never wired into a `dotnet test` step).

## Part B — Test Suite

### B1. Skipped/disabled tests — zero

Grepped for `[Fact(Skip`, `[Theory(Skip`, `Skip =`, `.Skip(`, `Ignore(`, `Assert.Inconclusive`, `xit`, `xdescribe` across all of `tests/`. The 4 raw hits are false positives (LINQ `.Skip()` for pagination, EF Core `warnings.Ignore(...)` transaction-warning suppression in test host setup). **Total: 1,646 `[Fact]`/`[Theory]` cases, all enabled.**

### B2. Timing-dependent tests — 4 occurrences, mostly justified

- `SecurityAlertBackgroundServiceTests.cs:78` — `Task.Delay(10)`, background-service loop tick yield.
- `NotificationIntegrationTests.cs:588` — used inside `Task.WhenAny` as a timeout companion, not a sleep-and-hope pattern.
- `RateLimitWindowSync.cs:41` — `Task.Delay(msRemaining + 250)`, a deliberate real-clock wait to cross a rate-limit window boundary. **AUD-11 (Low):** real wall-clock coupling is a latent flakiness risk on a busy CI runner, even though it's a purposeful, documented pattern rather than an accidental one.
- `StagingSmokeJourneyTests.cs:675` — retry backoff delay, appropriate for an E2E runner against a live deployment.

### B3. Assertion quality — spot-checked 4 files across 4 projects, all meaningful

- `CookieCsrfProtectionIntegrationTests.cs` — exercises the real HTTP pipeline end-to-end, reproduces an actual cross-site-attacker scenario, asserts specific status/error codes rather than "no exception thrown."
- `OtpServiceTests.cs` — meaningful behavioral assertions (format, range, hash correctness/mismatch, empty-input rejection).
- `AgencyLocationValidatorTests.cs` — proper mock-based validator tests with real hierarchical-consistency assertions.
- `PropertyConcurrencyTests.cs` (+ `UserAccountConcurrencyTests.cs`, `TransactionConcurrencyTests.cs`) — real optimistic-concurrency tests asserting `DbUpdateConcurrencyException` on a stale write against a real EF Core context.

### B4. CSRF and concurrency coverage vs. documentation — one real gap

**CSRF: well covered.** `CookieCsrfProtectionIntegrationTests.cs` (7 tests) covers token issuance, the Partitioned/CHIPS cookie attribute, missing-header rejection, invalid-token rejection, the cross-site-attacker-cannot-forge-header scenario, and the full valid round-trip. `HudhudNestApi.Architecture.Tests` additionally has `MiddlewareOrderGuardTests.cs`, `OptionsValidationGuardTests.cs`, `PublicEndpointPolicyTests.cs` covering CSRF-adjacent structural rules. **Not covered:** the new uncommitted `Program.cs` Production scheme-force fix (see AUD-01) — see `TEST-PLAN.md` for the specific test to add.

**Concurrency: AUD-03 (Medium-High, confirmed).** `tests/HudhudNestApi.Concurrency.Tests/ConcurrencySafetyRegressionTests.cs` is the entire project — 4 tests, and **none of them execute concurrent code**. All four read other files on disk (a k6 scenario script, a SQL integrity-check script, `PhoneAuthenticationWorkflow.cs`'s source text, `run-performance-tests.sh`) and assert those files still contain specific literal substrings — e.g. confirming the OTP-attempt-increment guard line hasn't been edited away. This proves the *source text* hasn't regressed; it does **not** exercise any actual concurrent execution path in-process. Real multi-replica/Redis-rate-limit-race and OTP-attempt-race behavior is only actually *executed* by the k6 scenarios inside `performance-validation.yml`, a separate workflow gated behind Docker/k6/Postgres/Redis containers — not part of a normal `dotnet test` run.

This is a **documented-capability-with-no-corresponding-test gap**: running `dotnet test tests/HudhudNestApi.Concurrency.Tests` locally or in a fast CI lane gives a false impression that concurrency safety is verified in-process, when it's actually a content-regression tripwire for the performance-test scripts. `docs/performance/load-testing-architecture.md` explicitly claims "Authentication races... invalid OTP attempt race" are covered — true in the k6/performance-validation sense, but misleading given this project's name and location. Worth flagging to whoever maintains `docs/testing/coverage-improvement-plan.md`'s Stage 3 entry ("Add behavior tests for failure paths... concurrency").

Distributed-lock coverage: not applicable — this codebase uses PostgreSQL advisory locks exclusively, not Redis locks (see Backend Audit Report §5). Redis-backed rate limiting **does** have real coverage: `RedisRateLimitingMiddlewareTests.cs`, `RateLimitingTests.cs`, `Architecture.Tests/Api/RedisRateLimitingGuardTests.cs`.

### B5. Project structure / isolation — sound

- `HudhudNestApi.Application.Tests.csproj` references only `HudhudNestApi.Application` + `HudhudNestApi.Domain` — no Infrastructure/DB.
- `HudhudNestApi.Infrastructure.Tests` has zero references to live DB/Redis/`WebApplicationFactory` — genuinely pure-unit with Moq.
- `HudhudNestApi.Integration.Tests` correctly pairs with CI's Postgres/Redis service containers; Redis is explicitly `FLUSHALL`'d between Auth and Integration test runs (`ci.yml:462-467, 481-486`) to prevent cross-suite state leakage.
- `HudhudNestApi.StagingSmokeTests`, `HudhudNestApi.Performance.Tests`, `HudhudNestApi.Concurrency.Tests` are correctly excluded from the main fast `ci.yml` run and delegated to their own specialized workflows, per the "every test project must have an owner" gate.

## Summary of Severities (CI/CD & Tests)

| ID | Severity | Finding |
|---|---|---|
| AUD-03 | Medium-High | `Concurrency.Tests` doesn't test concurrency — misleading name/doc claim. |
| AUD-04 | Medium | `production-deployment-gate`'s "no previous deploy" fallback can't distinguish first-deploy from a degraded API response. |
| AUD-09 | Low (needs verification) | `ci.yml`'s `pull-requests: write` — no located step requiring write. |
| AUD-10 | Low | `redis-ha-failover.yml` would currently fail if run — no HA-provider skip guard. |
| AUD-11 | Low | Wall-clock-dependent rate-limit test — latent flakiness risk. |
| AUD-15 | Informational (corrected) | Stale example SHA table in `github-actions-pinning.md`. |
| — | Informational (positive) | No `continue-on-error` anywhere; all actions SHA-pinned; explicit least-privilege permissions everywhere; zero skipped tests among 1,646; real (non-stub) production-gate/rollback logic including an actual Redis-outage chaos step. |
