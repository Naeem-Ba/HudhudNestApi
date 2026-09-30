# Test Plan — HudhudNestApi Audit Remediation

Tests required to validate each remediation item in `REMEDIATION-PLAN.md`. Commands assume the repository root as working directory and the existing test-project layout under `tests/`.

---

## T-01 — Production HTTPS-scheme-force regression test (for R-01)

**Target:** `HudhudNestApi/Program.cs:169-195` (uncommitted scheme-force middleware), `HudhudNestApi/Security/Csrf/CsrfExtensions.cs`.

**Why:** this is the fix for the `3b80fdc` Production incident; it currently has zero automated coverage and previously relied on manual "verify via curl in Production."

**Test data / setup:** requires a test host running with `ASPNETCORE_ENVIRONMENT=Production` (or equivalent `IsProduction()` override) plus `X-Forwarded-Proto: http` — reproducing exactly what Render/Cloudflare sends today. This depends on resolving the `TestApplication.CreateProduction()` Redis-config gap noted at `tests/HudhudNestApi.Integration.Tests/TestInfrastructure/TestApplication.cs:141` — do that first, or stand up a minimal isolated host for this one test if fixing the shared fixture is out of scope.

**Normal case:** request arrives with `X-Forwarded-Proto: https` (or no forwarded header, direct HTTPS) → antiforgery cookie is issued with `Secure=true`, no exception thrown, response is 200.

**Regression case (the actual incident):** request arrives exactly as Render's edge sends it today (`Request.IsHttps == false` pre-fix) → assert no `InvalidOperationException` from `DefaultAntiforgery.CheckSSLConfig`, antiforgery cookie is still issued correctly with `Secure=true`, response is 200 (not 500).

**Command:**
```bash
dotnet test tests/HudhudNestApi.Integration.Tests --filter "FullyQualifiedName~ProductionCsrfSchemeTests"
```
(new test class to be added; name illustrative)

**Success criteria:** test fails when run against a revert of the uncommitted fix (proving it actually catches the regression), passes with the fix applied.
**Failure criteria:** any `InvalidOperationException` from the antiforgery pipeline, or a non-Secure cookie issued in the simulated-Production case.

---

## T-02 — Lookup cache fail-open test (for R-03)

**Target:** `CommonLookupService.cs`, `DistributedCacheExtensions.GetOrCreateAsync`.

**Setup:** integration test with a real (or faulted) `IDistributedCache`/Redis connection — simulate a Redis exception on `GetAsync`/`SetAsync` (e.g. point at an unreachable Redis endpoint, or use a test double that throws `RedisConnectionException`).

**Normal case:** Redis available → lookup endpoint (e.g. `GET /api/amenities`) returns 200 with expected data, subsequent identical call is served from cache (verify via a call-count assertion on the underlying DB factory delegate).

**Edge case (the fix under test):** Redis unavailable → same endpoint still returns 200 with correct data, sourced from Postgres directly, not a 5xx.

**Command:**
```bash
dotnet test tests/HudhudNestApi.Integration.Tests --filter "FullyQualifiedName~CommonLookupServiceFailOpenTests"
```

**Success criteria:** 200 + correct payload in both cases.
**Failure criteria:** any 5xx when Redis is unavailable, or stale/incorrect data returned.

---

## T-03 — Concurrency test project re-scope (for R-04)

**If option (b) from the remediation plan is chosen (add real concurrency tests):**

**Target:** OTP-attempt increment race (`PhoneAuthenticationWorkflow.cs`'s guarded increment), currently only proven via static text-matching in `ConcurrencySafetyRegressionTests.cs`.

**Setup:** integration test spinning up N parallel requests (e.g. `Task.WhenAll` over 5-10 concurrent OTP-verify attempts) against a shared test user/OTP record.

**Normal case:** sequential attempts increment correctly up to the documented max (e.g. 3), then lock out.

**Race case:** concurrent attempts arriving simultaneously must not allow the attempt counter to be bypassed (i.e. total successful increments must not exceed what sequential execution would allow, even under a race) — this is the actual behavior the current script-text-matching test only indirectly implies.

**Command:**
```bash
dotnet test tests/HudhudNestApi.Concurrency.Tests --filter "FullyQualifiedName~OtpAttemptRaceTests"
```

**Success criteria:** attempt counter never exceeds the documented maximum regardless of request interleaving, across repeated runs (run at least 20 times in CI to catch intermittent races, or use a deterministic concurrency-testing approach if available).
**Failure criteria:** any run where more than the allowed number of attempts succeeds.

**If option (a) is chosen (rename only):** no new test required; update the project name and `docs/performance/load-testing-architecture.md` / `docs/testing/coverage-improvement-plan.md` to match, and add a CI check (or code comment) cross-referencing to the k6 scenario that actually covers this behavior.

---

## T-04 — Production-gate "no previous deploy" hardening (for R-05)

**Target:** `production-gate.yml`, `production-deployment-gate` job.

**Setup:** this is a workflow-level test, not a unit test — requires a scratch/test Render service or a mocked Render API response.

**Normal case:** Render API returns a genuine empty deploy history for a brand-new service → gate proceeds with a clear "first deploy" log line.

**Failure-simulation case:** Render API call fails or returns an unexpected/error response shape (simulate via a wrong `RENDER_SERVICE_ID` or a mocked 5xx from Render) → gate must block, not silently proceed as "first deploy."

**Command:** manual `workflow_dispatch` against a scratch environment, or a local shell-script unit test of the check logic if it's extracted into a standalone script.

**Success criteria:** the two cases are distinguishable in gate outcome.
**Failure criteria:** a simulated API failure is treated identically to a genuine first deploy.

---

## T-05 — Npgsql retry policy compatibility (for R-06)

**Target:** `PersistenceInfrastructureRegistration.cs`, `UnitOfWork.cs`'s explicit transaction methods.

**Setup:** enable `EnableRetryOnFailure()` in a test configuration; exercise both a simple single-`SaveChangesAsync` write path and the explicit `BeginTransactionAsync`/`CommitTransactionAsync` path used by `RegisterCommandHandler` and `DeleteUserCommandHandler`.

**Normal case:** both write paths succeed with retry enabled and no transient failure injected.

**Transient-failure case:** inject a simulated transient failure (e.g. via a test Npgsql connection wrapper or a network-partition test harness) during a write — assert the retry strategy recovers and completes successfully, and that the explicit-transaction path does not throw `InvalidOperationException: The configured execution strategy does not support user-initiated transactions` (a known EF Core pitfall when combining retry strategies with manual transactions).

**Command:**
```bash
dotnet test tests/HudhudNestApi.Integration.Tests --filter "FullyQualifiedName~NpgsqlRetryPolicyTests"
```

**Success criteria:** both scenarios pass; explicit-transaction code paths remain compatible with the retry strategy (likely requires wrapping `BeginTransactionAsync` calls in the execution strategy's `ExecuteAsync`, per EF Core's documented pattern for combining the two).
**Failure criteria:** any `InvalidOperationException` about execution strategy / user-initiated transactions, or a transient failure not being retried.

---

## T-06 — Background-sweep batch-lookup query count (for R-07)

**Target:** `ValuationSlaEnforcementService.cs`, `DistributionEngine.cs`.

**Setup:** integration test seeding N (e.g. 50) valuation invitations across a small number of distinct agencies, then running one sweep tick with DB query-count instrumentation (reuse the existing query-count assertion pattern already used for the HTTP-path CI gate, per `docs/performance/postgresql-query-analysis.md`).

**Before fix:** query count scales ~linearly with N (one `GetByIdAsync` per invitation).

**After fix:** query count is constant/O(distinct agencies), not O(N).

**Command:**
```bash
dotnet test tests/HudhudNestApi.Integration.Tests --filter "FullyQualifiedName~ValuationSlaEnforcementQueryCountTests"
```

**Success criteria:** query count assertion passes at the new, lower bound.
**Failure criteria:** query count still scales with invitation count.

---

## Regression Tests (run after any of the above changes)

For every item in this plan, run the full existing suite to confirm no regression:

```bash
dotnet test tests/HudhudNestApi.Application.Tests
dotnet test tests/HudhudNestApi.Architecture.Tests
dotnet test tests/HudhudNestApi.Auth.Tests
dotnet test tests/HudhudNestApi.Infrastructure.Tests
dotnet test tests/HudhudNestApi.Integration.Tests
dotnet test tests/HudhudNestApi.Observability.Tests
```

(`HudhudNestApi.Concurrency.Tests`, `HudhudNestApi.Performance.Tests`, `HudhudNestApi.StagingSmokeTests` run via their dedicated workflows, per the existing "every test project must have an owner" CI gate — not part of the fast local/PR loop.)

## Success / Failure Criteria Summary

| Test | Pass condition | Fail condition |
|---|---|---|
| T-01 | No exception + Secure cookie issued under simulated-Production forwarded-proto conditions | `InvalidOperationException` from antiforgery, or insecure cookie |
| T-02 | 200 + correct data with Redis down | Any 5xx with Redis down |
| T-03 | Attempt counter never exceeds max under concurrent load (option b) / doc+name accuracy (option a) | Counter exceeded, or doc still misleading |
| T-04 | API-failure and genuine-first-deploy cases produce different gate outcomes | Both treated identically |
| T-05 | Transient failure retried successfully in both plain and transactional write paths | Execution-strategy/transaction exception, or no retry occurs |
| T-06 | Query count constant regardless of batch size | Query count still linear in batch size |
