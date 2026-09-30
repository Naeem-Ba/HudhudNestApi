# Remediation Plan — HudhudNestApi

Ordered by priority. Each item lists the issue ID from `FULL-CODE-AUDIT-REPORT.md` §5, the fix, dependencies, complexity, required tests, acceptance criteria, rollback plan, and risks. No code changes were made as part of producing this plan — it is a proposal for a human/maintainer to execute and review.

---

## Urgent (before the next Production deploy)

### R-01 — Commit and test the CSRF/HTTPS-scheme fix (AUD-01)
- **Issue:** `HudhudNestApi/Program.cs` and `HudhudNestApi/Security/Csrf/CsrfExtensions.cs` carry a correct, working-tree-only fix for the Production incident caused by merged commit `3b80fdc`. Uncommitted work can be lost; the fix has zero automated regression coverage.
- **Fix:**
  1. Commit the two files with a message documenting *why* (the `3b80fdc` regression), not just *what*, so the reasoning survives outside a code comment.
  2. Add a regression test proving the scheme-force behavior (see `TEST-PLAN.md` T-01).
  3. Either fix or explicitly document the `TestApplication.CreateProduction()` gap (`tests/HudhudNestApi.Integration.Tests/TestInfrastructure/TestApplication.cs:141`) that previously blocked a Production-mode antiforgery regression test.
- **Dependencies:** none — can proceed immediately.
- **Complexity:** Low (commit + one focused test).
- **Tests required:** T-01 (see Test Plan).
- **Acceptance criteria:** commit lands on the branch; new test fails against a revert of the fix and passes with it in place; `TestApplication.CreateProduction()` either works or its limitation is documented in a code comment referencing this remediation item.
- **Rollback plan:** if the committed fix regresses something else, `git revert` the single commit — it's isolated to two files.
- **Risk of applying the fix:** low — it was already reasoned through and compiles; the residual risk is the *unverified infra assumption* in R-02 below, not the fix itself.
- **Human decision needed:** no — this is a mechanical "finish what's already started" action.

### R-02 — Verify the Production scheme-force trust assumption (AUD-05)
- **Issue:** `Program.cs:169-195` forces every Production request to `Request.Scheme = "https"`. This is correct only if Cloudflare/Render genuinely never exposes a plain-HTTP path directly to Kestrel.
- **Fix:** confirm (via Render/Cloudflare configuration, not code) that the origin cannot be reached over `http://` directly, only via the Cloudflare edge. If it can be, add explicit `X-Forwarded-Proto` validation that rejects or redirects rather than blindly trusting the header. Also confirm `KnownProxies`/`KnownNetworks` in the Production `ForwardedHeaders` config correspond to Cloudflare's actual published IP ranges, not an overbroad range.
- **Dependencies:** R-01 should land first (same file area).
- **Complexity:** Low if the infra is already correctly configured (just confirm and document); Medium if a code-level guard needs to be added.
- **Tests required:** none automatable from this repo alone (infrastructure-level check); if a code guard is added, cover it with an integration test simulating a spoofed `X-Forwarded-Proto`.
- **Acceptance criteria:** written confirmation (e.g. a short note in `docs/architecture/` or an ADR) that the trust assumption holds, with the actual `KnownProxies`/`KnownNetworks` values referenced (not committed if sensitive).
- **Rollback plan:** N/A (verification task, not a code change) unless a guard is added, in which case revert the guard commit.
- **Risk:** if skipped, a misconfigured edge could allow Secure/SameSite=None cookies to be set over an insecure channel while the app believes it's HTTPS.
- **Human decision needed:** yes — requires access to Render/Cloudflare configuration this audit did not have.

---

## Short-term (next 1-2 sprints)

### R-03 — Make the lookup cache fail open on Redis errors (AUD-02)
- **Fix:** wrap `CommonLookupService`'s Redis calls (or `DistributedCacheExtensions.GetOrCreateAsync` itself) in try/catch, falling back to the DB factory on any Redis exception — mirroring the pattern already used in `CachedAppReleaseCacheService.cs:37-67`.
- **Dependencies:** none.
- **Complexity:** Low — one well-understood pattern applied to one shared helper.
- **Tests required:** T-02.
- **Acceptance criteria:** a lookup endpoint (e.g. amenities) returns 200 with correct data when Redis is simulated as unavailable, not a 5xx.
- **Rollback plan:** revert the single commit; behavior returns to current (fail-closed) state.
- **Risk:** near-zero — this closes a gap the project's own docs already call out as intended behavior.

### R-04 — Rename or re-scope `HudhudNestApi.Concurrency.Tests` (AUD-03)
- **Fix:** either (a) rename the project/tests to reflect what they actually do (e.g. `ScriptIntegrityRegressionTests`), or (b) add real in-process concurrency tests (e.g. two parallel requests racing an OTP-attempt increment, asserting only one succeeds) alongside the existing script-integrity checks, and update `docs/testing/coverage-improvement-plan.md` / `docs/performance/load-testing-architecture.md` to accurately describe where concurrency is actually exercised (k6/performance-validation) vs. what this project checks.
- **Dependencies:** none.
- **Complexity:** Medium if adding real tests (option b); Low if just renaming/documenting (option a).
- **Tests required:** T-03.
- **Acceptance criteria:** the project's name and documentation both accurately describe its contents; a reader can no longer conclude "concurrency is unit-tested" when it isn't.
- **Rollback plan:** trivial (rename/doc-only, or additive tests that can be deleted).
- **Risk:** none.

### R-05 — Harden the "no previous deploy" fallback in `production-gate.yml` (AUD-04)
- **Fix:** distinguish "genuinely first deploy" from "Render API returned empty/degraded response" — e.g. check the Render service's creation timestamp, or require an explicit manual override input rather than auto-proceeding with only a warning.
- **Dependencies:** none.
- **Complexity:** Medium (requires understanding Render's API response shape for a truly-empty deploy history vs. an error).
- **Tests required:** T-04 (workflow-level, may need a dry-run/dispatch test against a scratch Render service).
- **Acceptance criteria:** a simulated Render API failure during this check causes the gate to block, not silently proceed.
- **Rollback plan:** revert the workflow change; prior (less strict) behavior returns.
- **Risk:** could over-block genuine first deploys if the creation-date heuristic is wrong — test against a real first-deploy scenario in a non-production service first.
- **Human decision needed:** yes — someone with Render API access should confirm the exact response shape for each case.

### R-06 — Apply Npgsql retry policy (AUD-06, pre-existing item F6)
- **Fix:** add `EnableRetryOnFailure()` to the `UseNpgsql(...)` call in `PersistenceInfrastructureRegistration.cs`, with a bounded retry count/delay appropriate for the hosting environment.
- **Dependencies:** none — already tracked in `DATABASE-PRODUCTION-READINESS.md`, this just executes it.
- **Complexity:** Low.
- **Tests required:** T-05 (verify retry doesn't break explicit-transaction code paths that manage their own execution strategy).
- **Acceptance criteria:** a simulated transient connection failure during a write is retried and succeeds, rather than surfacing as a 500.
- **Rollback plan:** revert the single-line config change.
- **Risk:** Low-Medium — EF Core's retrying execution strategy has known interactions with manually-managed transactions (`BeginTransactionAsync`); the existing `UnitOfWork` transaction code must be reviewed for compatibility before enabling this, since retry strategies typically require the transaction itself to be wrapped in the strategy's `ExecuteAsync`.

---

## Medium-term (this quarter)

### R-07 — Batch background-sweep lookups (AUD-07)
- **Fix:** in `ValuationSlaEnforcementService.cs` and `DistributionEngine.cs`, replace per-item `GetByIdAsync` calls inside loops with a single batched `GetByIdsAsync(distinctIds)` lookup up front.
- **Complexity:** Low-Medium.
- **Tests required:** T-06.
- **Acceptance criteria:** DB query count for a batch of N invitations/winners drops from O(N) to O(1) for the agency/skip-reason lookups, verified via a query-count assertion in an integration test.
- **Rollback plan:** revert; behavior is functionally identical either way, just slower.
- **Risk:** Low.

### R-08 — Confirm `pull-requests: write` necessity in `ci.yml` (AUD-09)
- **Fix:** either locate and document the step using this permission, or narrow it to `pull-requests: read` (or remove it) if unused.
- **Complexity:** Low (investigation) + trivial (one-line change).
- **Tests required:** run the workflow once after narrowing to confirm nothing depending on write access breaks.
- **Acceptance criteria:** `permissions:` block reflects only what's actually used.
- **Rollback plan:** revert the permission narrowing if something breaks.
- **Risk:** near-zero.

### R-09 — Fix or clearly mark `redis-ha-failover.yml` as non-functional today (AUD-10)
- **Fix:** add the same "no HA provider configured → skip with explanation" guard that `production-gate.yml`'s `validate-redis-ha-topology` job already has, or add a header comment stating the workflow requires a managed Redis HA provider not yet provisioned.
- **Complexity:** Low.
- **Tests required:** dispatch the workflow once after the fix to confirm it either skips cleanly or runs against a real HA topology when available.
- **Acceptance criteria:** running this workflow no longer fails with a confusing error when no HA provider exists.
- **Rollback plan:** trivial.
- **Risk:** none.

### R-10 — Reduce wall-clock coupling in the rate-limit window test (AUD-11)
- **Fix:** inject a clock abstraction into the rate-limit window test instead of a real `Task.Delay` crossing a real window boundary, or increase the margin/isolate the test to reduce flakiness risk on a busy runner.
- **Complexity:** Medium (may require adding a testable clock seam to `RedisRateLimitingMiddleware` if one doesn't already exist).
- **Tests required:** the modified test itself, run repeatedly under load to confirm reduced flakiness.
- **Acceptance criteria:** test no longer depends on real elapsed time to prove correctness, or documented as an accepted, bounded risk if a clock seam isn't worth adding.
- **Rollback plan:** revert to the current wall-clock version.
- **Risk:** low; purely a test-quality improvement.

---

## Long-term / improvements (not urgent)

### R-11 — Remove unused `MessagePack` dependency (AUD-08)
- **Fix:** remove the `MessagePack` package reference from `HudhudNestApi.csproj` and `Directory.Packages.props` if confirmed genuinely unused (re-confirm with a fresh grep immediately before removal, since new code may have started using it since this audit).
- **Complexity:** Low.
- **Tests required:** full build + test suite after removal to confirm nothing transitively depended on it.
- **Acceptance criteria:** build succeeds, all tests pass, package no longer listed.
- **Rollback plan:** re-add the package reference.
- **Risk:** near-zero.

### R-12 — Document the Staging single-replica constraint for `StagingSmokeMediaStorageService` (AUD-12)
- **Fix:** add a note to `docs/testing/staging-smoke-current-state.md` stating this in-memory media double requires Staging to remain single-replica, or make it Redis-backed if Staging is ever scaled.
- **Complexity:** Low (doc-only) or Medium (if made Redis-backed).
- **Tests required:** none for the doc-only option.
- **Acceptance criteria:** the constraint is written down somewhere a future engineer scaling Staging would find it.
- **Rollback plan:** N/A.
- **Risk:** none.

### R-13 — Add a short ADR for the Production scheme-force pattern
- **Fix:** given the repeated-hotfix history of this exact area, write a one-page ADR explaining why `Request.Scheme` is force-set in Production, what infra assumption it depends on (R-02), and what would need to change if a second ingress path is ever added.
- **Complexity:** Low (documentation only).
- **Dependencies:** should follow R-01/R-02.
- **Acceptance criteria:** ADR exists in `docs/architecture/`.
- **Risk:** none.

---

## Dependency Graph Summary

```
R-01 (commit+test CSRF fix) ──┬─→ R-02 (verify trust assumption) ─→ R-13 (ADR)
                               │
R-03, R-04, R-06, R-07, R-08,  │  (independent, can proceed in any order)
R-09, R-10, R-11, R-12  ───────┘
```

No item in this plan blocks another except R-01 → R-02 → R-13 (same code area, sequential for clarity, not a hard technical dependency).
