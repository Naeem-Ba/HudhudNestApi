# Full Code Audit Report — PropertyApi

- **Date:** 2026-09-18
- **Branch audited:** `hotfix/revert-csrf-securepolicy-always` (up to date with `origin/master`, plus uncommitted working-tree changes — see §7)
- **Scope of authority:** read-only review. No code was modified, no branches/files were deleted, no merge/push/deploy was performed.
- **Method:** five independent focused passes (architecture, security/API, database & Redis, CI/CD & tests, dependencies & git history), each instructed to read the project's existing documentation first and verify code against it rather than assume defects.

## 1. Executive Summary

PropertyApi is a .NET 8 "Clean Architecture" backend (Domain / Application / Infrastructure / API) for a real-estate platform, with an unusually mature set of self-produced production-readiness and architecture documents (`docs/SECURITY-PRODUCTION-READINESS.md`, `docs/AUTHENTICATION-PRODUCTION-READINESS.md`, `docs/DATABASE-PRODUCTION-READINESS.md`, `docs/architecture/ARD/ARD-PropertyApi.md`, several Redis HA/failure-policy docs), most dated within the last two weeks. This audit treated those documents as a baseline to verify, not to trust blindly, and focused new effort where the code could plausibly have drifted from what the docs claim.

**Overall state: no Critical or newly-discovered High-severity defects were found.** The single most important observation is **process-level, not code-level**: the current branch carries a real, correct, but **uncommitted** fix for a Production incident (§7), and that fix has no automated regression test. Everything else is Medium/Low/Informational — mostly known, already-tracked risks (some already documented as open items in the project's own readiness reports) plus a handful of newly found gaps in caching fail-open behavior, a mislabeled test project, and three stale documentation tables (now corrected as part of this audit, see §9).

There is **no frontend code in this repository** — the Angular SPA lives in a separate repository/path, confirmed by prior session memory and by the absence of any Angular/TypeScript/frontend project in this repo's structure. `FRONTEND-AUDIT-REPORT.md` documents this explicitly rather than fabricating findings against code that isn't here.

## 2. Scope of the Audit

- **Backend:** PropertyApi.Domain, PropertyApi.Application, PropertyApi.Infrastructure, PropertyApi (API/composition root) — architecture, API/endpoints, auth/authz, database, Redis, security.
- **Tests:** all 9 projects under `tests/` (Application, Architecture, Auth, Concurrency, Infrastructure, Integration, Observability, Performance, StagingSmokeTests).
- **CI/CD:** all 9 workflows under `.github/workflows/`.
- **Dependencies:** `Directory.Packages.props`, `Directory.Build.props`, `NuGet.config`, `PropertyApi/packages.lock.json`.
- **Git history:** full `git log` archaeology for recurring bug patterns, reverts, and hotfixes.
- **Frontend:** out of scope — not present in this repository (see §1).

## 3. Documentation Read

All of the following were read in full (not excerpted) by the relevant audit pass before forming judgments:
`docs/architecture/*.md` (incl. `ARD/ARD-PropertyApi.md`), `docs/phase-b-*.md`, `docs/development/central-package-management.md`, `docs/SECURITY-PRODUCTION-READINESS.md`, `docs/AUTHENTICATION-PRODUCTION-READINESS.md`, `SECURITY_HARDENING_REPORT_AR.md`, `docs/password-policy.md`, `docs/security/*.md`, `docs/phone-password-authentication-and-reverification.md`, `docs/database/erd.md`, `docs/DATABASE-PRODUCTION-READINESS.md`, `docs/performance/postgresql-query-analysis.md`, `docs/REDIS-HA.md`, `docs/operations/redis-*.md`, `docs/testing/*.md`, `docs/performance/*.md`, `docs/operations/database-backup-restore-runbook.md`, `docs/operations/rpo-rto.md`.

## 4. Project Components (as verified against code, not assumed)

| Area | Technology / Version |
|---|---|
| Backend runtime | .NET 8 (LTS), ASP.NET Core |
| Backend packages | `Microsoft.AspNetCore.*` / `Microsoft.EntityFrameworkCore.*` pinned to `8.0.31`; `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11`; `StackExchange.Redis 2.13.17` |
| Database | PostgreSQL via EF Core, central package management (`Directory.Packages.props`) |
| Cache / rate limiting / output cache | Redis (`StackExchange.Redis`), single shared instance today — key-prefix isolation only (ADR status: "proposed" for true workload isolation) |
| Auth | Cookie-based session + JWT bearer, ASP.NET Core Identity, CSRF via antiforgery + custom cookie handling, Google/Apple social sign-in |
| Media storage | Cloudinary |
| SMS/OTP | Twilio |
| Locking | PostgreSQL advisory locks (`pg_advisory_xact_lock` / `pg_try_advisory_lock`) — **no Redis distributed locks are used anywhere in the codebase**, a deliberate and sound design choice |
| Test frameworks | xUnit 2.9.3, Moq, `Microsoft.AspNetCore.Mvc.Testing`, SignalR.Client (integration only) |
| CI/CD | GitHub Actions, 9 workflows, all external actions pinned to full commit SHA |
| Deployment target | Render (per prior session memory / docs), fronted by Cloudflare |
| Frontend | **Not in this repository** — separate Angular SPA project |

## 5. Issues Found — Consolidated Register

Severity definitions: **Critical** = active exploit / data loss risk; **High** = serious defect requiring near-term fix; **Medium** = real gap, bounded impact; **Low** = minor/latent risk; **Informational** = no action required, noted for awareness or as a positive finding.

| ID | Severity | Area | File(s) | Summary |
|---|---|---|---|---|
| AUD-01 | High (process) | Security / Release | `PropertyApi/Program.cs`, `PropertyApi/Security/Csrf/CsrfExtensions.cs` (uncommitted) | Working-tree-only fix for a real Production incident caused by commit `3b80fdc`; correct but uncommitted and untested. See §7. |
| AUD-02 | Medium | Database / Redis | `PropertyApi.Infrastructure/Lookups/CommonLookupService.cs`, `Caching/DistributedCacheExtensions.cs` | Lookup cache does not fail open on Redis errors, contradicting documented policy. |
| AUD-03 | Medium | CI/CD, Tests | `tests/PropertyApi.Concurrency.Tests/ConcurrencySafetyRegressionTests.cs` | Project named "Concurrency.Tests" does not execute any concurrent code — it's a text-regression guard on other scripts. |
| AUD-04 | Medium | CI/CD | `.github/workflows/production-gate.yml` | "No previous deploy" fallback can't distinguish a genuine first deploy from a degraded Render API response. |
| AUD-05 | Medium (trust boundary, needs verification) | Security / Architecture | `PropertyApi/Program.cs:169-195` | Production unconditionally forces `Request.Scheme = "https"`; correctness now depends entirely on Cloudflare/Render never exposing a plain-HTTP path to Kestrel. |
| AUD-06 | Low-Medium (pre-existing, corroborated) | Database | `PropertyApi.Infrastructure/Persistence/PersistenceInfrastructureRegistration.cs` | No `EnableRetryOnFailure` on the Npgsql EF Core provider (already tracked as open item F6 in `DATABASE-PRODUCTION-READINESS.md`). |
| AUD-07 | Low | Database | `ValuationSlaEnforcementService.cs`, `DistributionEngine.cs` | Per-item DB/notification calls inside background sweep loops (N+1 shape, bounded/background only). |
| AUD-08 | Low | Dependencies | `PropertyApi/PropertyApi.csproj` | `MessagePack` package referenced but unused anywhere in source. |
| AUD-09 | Low | CI/CD | `.github/workflows/ci.yml` | `pull-requests: write` permission granted with no located step that requires write access (needs verification). |
| AUD-10 | Low | CI/CD | `.github/workflows/redis-ha-failover.yml` | Lacks the "no HA provider configured" skip guard that `production-gate.yml` has; would fail if run today. |
| AUD-11 | Low | Tests | `tests/PropertyApi.Integration.Tests/.../RateLimitWindowSync.cs:41` | Real wall-clock `Task.Delay` to cross a rate-limit window — latent flakiness risk on a busy runner. |
| AUD-12 | Low | Architecture | `PropertyApi.Infrastructure/Media/StagingSmokeMediaStorageService.cs` | In-memory singleton would break across multiple replicas if Staging is ever scaled beyond 1 instance (currently gated to Staging test-support flag only). |
| AUD-13 | Informational | Documentation | `docs/architecture/ARD/ARD-PropertyApi.md` (R11) | Marked "unconfirmed" a question the code already answers (`ValuationInquiryExpiryHostedService` does use `BackgroundJobLock`). **Corrected as part of this audit — see §9.** |
| AUD-14 | Informational | Documentation | `docs/development/central-package-management.md` | Test-stack version table did not match `Directory.Packages.props`. **Corrected as part of this audit — see §9.** |
| AUD-15 | Informational | Documentation | `docs/security/github-actions-pinning.md` | Example pinned-SHA table did not match the SHAs actually used in workflows. **Corrected as part of this audit — see §9.** |
| AUD-16 | Informational | Architecture | — | No Redis distributed locks anywhere; all locking is via PostgreSQL advisory locks — sound design, avoids the classic Redis-lock-without-TTL deadlock hazard entirely. |
| AUD-17 | Informational | Security | `PropertyApi/Security/RateLimiting/RedisRateLimitingMiddleware.cs`, `PropertyApi.Infrastructure/Identity/Services/CachedSecurityStampValidator.cs` | Rate limiting fails closed (503) and the security-stamp cache fails secure to the DB on Redis errors — both genuinely implemented, matching documented policy. |
| AUD-18 | Informational | CI/CD | all workflows | No `continue-on-error: true` anywhere; all external actions SHA-pinned; explicit least-privilege `permissions:` blocks; zero skipped tests among 1,646 `[Fact]`/`[Theory]` cases; `production-gate.yml` runs a real Redis-outage chaos test, not a stub. |

Full detail for each finding, including reproduction/evidence and suggested fixes, is in `BACKEND-AUDIT-REPORT.md` and `CI-CD-AUDIT-REPORT.md`.

## 6. General Risks

- **Single point of Redis workload contention**: output cache, `IDistributedCache`, and Redis rate limiting all share one Redis instance/connection multiplexer today (key-prefix isolation only). This is an accepted, documented risk (ADR status "proposed"), not a surprise — but it means a burst on one workload (e.g. rate-limit checks) can degrade another (e.g. lookup caching) under load.
- **CSRF/cookie configuration is the single most recurring change area in this repository's history** (at least 4 touches: `da7307a`, `0043b2a`, `3b80fdc`, and the current uncommitted branch work), driven by Render/Cloudflare's unreliable `Request.IsHttps` detection. The current in-progress fix looks structurally correct (§7) but the area's history suggests it deserves a permanent regression test and a short ADR, not another silent patch.
- **Documentation drift**: three docs were found materially out of sync with the code they describe (§9). None caused a functional problem (the code was correct; the docs were wrong), but each represents a moment where a future engineer could waste time chasing a non-issue or trusting a stale example.

## 7. Special Focus: CSRF/Cookie State on This Branch

Direct answer to the question this branch's name raises — **is the CSRF/cookie code in a broken or half-applied state?**

**No — it is correct and internally consistent, but it is uncommitted, in-progress work, not a landed fix.**

- `git status` shows two modified, unstaged files: `PropertyApi/Program.cs` and `PropertyApi/Security/Csrf/CsrfExtensions.cs`.
- Commit `3b80fdc` (already merged to `master` via PR #154) hard-coded `SecurePolicy = Always` for the antiforgery cookie in Production, intended to fix a missing-CSRF-cookie bug. According to the working tree's own code comments, **this merged fix caused real Production 500 errors within minutes of deployment**: `DefaultAntiforgery.CheckSSLConfig` throws when `SecurePolicy = Always` and `Request.IsHttps` is false — and `Request.IsHttps` was still unreliably false in Production, so the underlying detection problem was never actually fixed, only its symptom was papered over.
- The current uncommitted working-tree fix addresses the root cause instead: `Program.cs` now forces `context.Request.Scheme = "https"` for every request when `IsProduction()`, placed immediately after `UseForwardedHeaders()`; `CsrfExtensions.cs` reverts `SecurePolicy` to plain `CookieSecurePolicy.SameAsRequest`, now safe because `Request.IsHttps` is reliably true. `CsrfController.cs`'s separate hard-coded `Secure = true` (which never went through `CheckSSLConfig`) was correctly left untouched.
- `dotnet build` succeeds with these changes. No leftover dead branch or contradictory conditional was found between the three files.

**Two concrete gaps remain:**
1. **Not committed.** This fix currently exists only in the working tree and could be lost.
2. **No automated regression test.** The `3b80fdc` commit message itself disclosed an abandoned attempt to add a `TestApplication.CreateProduction()` regression test (blocked by a pre-existing Redis-config gap in that test host, confirmed still open at `tests/PropertyApi.Integration.Tests/TestInfrastructure/TestApplication.cs:141`). The exact code path this fix depends on — the `IsProduction()` branch combined with a real-HTTPS `SameAsRequest` policy — has zero automated coverage today, relying on the same "verify in Production" methodology that already caused this incident once.

**Recommendation:** commit these two files with a message that documents the `3b80fdc` regression (this fact currently only exists as a code comment on uncommitted work — commit it before it's lost), and close the `TestApplication.CreateProduction()` gap (or add an alternative automated proof) before relying on this fix again. See `REMEDIATION-PLAN.md` item R-01.

## 8. Constraints Encountered

- No network egress was available to run a live CVE/vulnerability scan (`dotnet list package --vulnerable`) or to confirm GitHub Actions SHA-to-tag resolution via `git ls-remote`; dependency staleness assessments are based on version-number judgment against a January 2026 knowledge cutoff and are flagged "needs verification" where relevant.
- No credentials were available (nor sought) to inspect live Render/Cloudflare configuration (`KnownProxies`/`KnownNetworks` values, actual Staging replica count, live Redis topology) — these are noted as "needs verification against infrastructure" rather than asserted either way.
- The audit did not attempt to run the full test suite or CI pipeline live; findings on test behavior are based on static reading of test source and prior CI run evidence embedded in commit history/docs.
- This was a static, read-only review, not a penetration test — no exploitation was attempted against any running system.

## 9. Documentation Updated

Three documentation inaccuracies were found with direct code evidence and corrected as part of this audit (content changes only, no speculative additions):

1. **`docs/architecture/ARD/ARD-PropertyApi.md`** — Risk R11 asked whether `ValuationInquiryExpiryHostedService` uses `BackgroundJobLock`; code confirms it does (`PropertyApi.Infrastructure/Valuation/ValuationInquiryExpiryHostedService.cs:95-121`). Updated from "unconfirmed" to confirmed, with the file reference.
2. **`docs/development/central-package-management.md`** — "Test Stack" table listed `Microsoft.NET.Test.Sdk 17.8.0` / `xunit 2.6.6` / `xunit.runner.visualstudio 2.5.6` / `coverlet.collector 6.0.0`; actual centrally-pinned versions in `Directory.Packages.props` are `18.10.0` / `2.9.3` / `4.0.0` / `10.0.1`. Table updated to match.
3. **`docs/security/github-actions-pinning.md`** — Example pinned-SHA table listed SHAs for `checkout@v5`, `setup-dotnet@v5`, `upload-artifact@v6`; the workflows currently and consistently use `checkout@...ba90b1 # v7.0.1`, `setup-dotnet@...06c68 # v6.0.0`, `upload-artifact@...fc6a0a # v7.0.1` across all 9 workflow files. Table updated to match current pins. (Enforcement script behavior is unaffected — it checks SHA *format*, not tag correctness.)

No other documentation was changed. Items still open for a human decision (e.g., whether to add a Staging replica-count note to `docs/testing/staging-smoke-current-state.md` per AUD-12) are listed as recommendations in `REMEDIATION-PLAN.md`, not applied automatically, since they would require confirming current infrastructure state rather than correcting a factual code/doc mismatch.

## 10. Summary — Files Produced

- `docs/audit/FULL-CODE-AUDIT-REPORT.md` (this file)
- `docs/audit/BACKEND-AUDIT-REPORT.md`
- `docs/audit/FRONTEND-AUDIT-REPORT.md`
- `docs/audit/CI-CD-AUDIT-REPORT.md`
- `docs/audit/REMEDIATION-PLAN.md`
- `docs/audit/TEST-PLAN.md`
