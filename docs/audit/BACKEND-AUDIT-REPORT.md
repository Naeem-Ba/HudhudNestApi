# Backend Audit Report — PropertyApi

Companion detail report to `FULL-CODE-AUDIT-REPORT.md`. Covers architecture, API, auth/authz, database, Redis, performance, security, and backend tests. Read-only audit; no code changed.

## 1. Architecture Assessment

**Layer separation is real, independently verified, not just convention.** `PropertyApi.Domain.csproj` has zero package or project references; a full scan of Domain `.cs` files shows only BCL and Domain namespaces — no EF Core, MediatR, or ASP.NET Core leakage. `PropertyApi.Application.csproj` references only Domain, using MediatR/FluentValidation but no EF Core package. Infrastructure and the API layer correctly gain EF Core/Identity/Redis only where needed. A dedicated architecture test (`RepositoryWorkflowBoundaryTests`, referenced in `docs/phase-b-application-orchestration-boundaries.md`) enforces the repository boundary at build/test time, not just by convention.

**Transactions are explicit and correctly scoped.** Verified in `RegisterCommandHandler` (`PropertyApi.Application/Auth/Commands/Register/RegisterCommand.cs:184-300`) and `DeleteUserCommandHandler.AnonymizeAndFinalizeAsync` (`PropertyApi.Application/Users/Commands/DeleteUser/DeleteUserCommandHandler.cs:192-269`): `BeginTransactionAsync` → mutate → `SaveChangesAsync` → `CommitTransactionAsync`, with try/catch rollback, and inline comments explaining which cleanup steps (refresh-token revocation, cache invalidation, Cloudinary delete, audit log) are deliberately outside the transaction as best-effort.

**Optimistic concurrency is implemented and exercised, not aspirational.** PostgreSQL `xmin`-based concurrency tokens exist on `Property`, `Transaction`, `UserAccount`, `ValuationInquiry`, `ValuationOfficeInvitation` (added via dedicated migrations), with dedicated integration tests (`PropertyConcurrencyTests.cs`, `TransactionConcurrencyTests.cs`, `UserAccountConcurrencyTests.cs`) and centralized `DbUpdateConcurrencyException` handling in `ExceptionHandlingMiddleware`. `UnitOfWork.cs` additionally exposes `SaveChangesDroppingConcurrencyConflictsAsync`/`TrySaveChangesAsync` specifically for batch sweeps that must tolerate individual row races without failing the whole batch.

**Multi-replica-safe background jobs, deliberately engineered.** `BackgroundJobLock` (`PropertyApi.Infrastructure/Persistence/BackgroundJobLock.cs`) wraps `pg_try_advisory_lock`/`pg_advisory_unlock`, with a documented rationale for why naive multi-instance timers would double-process rows. All 8 hosted services were checked; the two not using this lock (`ProductionStartupValidator`, `SecurityAlertBackgroundService`) have explicit, sound justification in code comments (boot-time self-validation; in-process channel with nothing cross-instance to coordinate). Every hosted service's `ExecuteAsync` loop catches and logs exceptions without dying, correctly distinguishes `OperationCanceledException` from real failures, and `SecurityAlertBackgroundService` overrides `StopAsync` to drain its queue rather than dropping in-flight work.

**DI lifetimes are correct everywhere checked.** No Scoped-into-Singleton captures were found. Singletons are either genuinely stateless (folder/normalizer/policy helpers) or intentionally stateful in a way that's safe per-instance (bounded `Channel`, circuit breaker). The "one singleton behind two interfaces" pattern for `SecurityAlertBackgroundService`/`ISecurityAlertDispatcher` is correctly implemented with an inline comment explaining why `AddHostedService<T>()` alone would have created a second, disconnected instance.

**File sizes / god-class check:** the largest non-migration files (`NotificationService.cs` 777 lines, `ValuationSlaEnforcementService.cs` 549 lines) were spot-checked and are cohesive around one responsibility with small, focused constructor dependency lists (4-6 each).

### Architecture findings

| Severity | File | Finding |
|---|---|---|
| Informational | `docs/architecture/ARD/ARD-PropertyApi.md:545` (R11) | Marked "unconfirmed" a question code already answers — corrected in this audit (§9 of the full report). |
| Low | `PropertyApi.Infrastructure/Media/StagingSmokeMediaStorageService.cs:9` | In-memory `ConcurrentDictionary`-backed singleton smoke double for media storage; would silently misbehave across multiple replicas. Correctly gated behind `Staging:TestSupport:UseInMemoryMedia` so it cannot reach Production, and Staging is currently a single instance — but this constraint is not written down anywhere. **Recommendation:** add a one-line note to `docs/testing/staging-smoke-current-state.md` stating Staging must stay single-replica while this flag is enabled, or make the double Redis-backed if Staging is ever scaled. |
| Low | `PropertyApi/Program.cs:169-195` | Global `Request.Scheme = "https"` override in Production is a blunt fix (see AUD-05 in the full report / security section below) — architecturally sound as a stopgap but deserves a short ADR given the area's repeated-hotfix history, so the next engineer doesn't rediscover and silently re-patch it. |
| Informational | `PropertyApi.Infrastructure/Auth/AppleTokenVerifier.cs:21,27` | Apple JWKS cache uses per-instance `IMemoryCache`; not a correctness issue for multi-replica (each instance just re-fetches independently), only a minor efficiency note. TTL/eviction not verified. |
| Informational | — | Git churn is concentrated in CSRF/cookie composition-root config (4+ touches), not in Domain/Application/Infrastructure business logic, which shows normal feature-driven history with no thrashing. |

## 2. API / Endpoints

- **AuthN/AuthZ:** JWT/cookie auth configured in `Program.cs`; `[Authorize]` used consistently across controllers; role-based checks for admin-only endpoints verified directly (e.g. `AdminAppReleasesController` is `[Authorize(Roles = Admin)]` at the controller level). IDOR/BOLA ownership checks, refresh-token rotation, and lockout policy were **not re-derived from scratch this session** — they were already covered with file:line evidence and passing tests in the existing `docs/AUTHENTICATION-PRODUCTION-READINESS.md` (dated 2026-09-05) and `docs/SECURITY-PRODUCTION-READINESS.md` (2026-09-04); this audit spot-checked rather than duplicated that work and found nothing contradicting it.
- **New feature reviewed in full** (merged today, not covered by the existing docs): App Update Management (`AdminAppReleasesController.cs`, `AppUpdatesController.cs`). Admin CRUD correctly role-gated; public `check` endpoint is `[AllowAnonymous]` + rate-limited, exposes nothing sensitive; `StoreUrl` validated as an absolute http/https URL via FluentValidation and never fetched or redirected-to server-side (no SSRF/open-redirect surface); pagination clamped 1–100; command DTOs are explicit allowlists (no mass-assignment risk). **No issues found.**
- **CORS:** spot-checked `CorsRegistration.cs` — confirmed the documented allowlist-or-throw pattern still matches current code.
- **Middleware order:** verified end-to-end in `Program.cs:161-229` — `UseForwardedHeaders → [prod scheme-force] → HSTS/HttpsRedirect → ExceptionHandlingMiddleware → SecurityHeaders → Swagger(gated) → StaticFiles → CORS → Authentication → RateLimiter → CookieCsrfProtection → Authorization → MapControllers`. Exception handler registered early enough to catch downstream errors; CORS precedes Authentication correctly for credentialed requests. No ordering defect.
- **Secrets:** no real secrets committed. `appsettings.json` / `.Development.json` use empty placeholders for all sensitive fields (Jwt key, Cloudinary, Email, Redis, DB connection string — fails fast if unset). `appsettings.Testing.json` contains only local/CI-only dummy values consistent with a test fixture (localhost-only test DB password, clearly-labeled `TEST_ONLY_SECRET_KEY_...`).

## 3. CSRF / Cookie State

See `FULL-CODE-AUDIT-REPORT.md` §7 for the full account. Summary: the branch's uncommitted working-tree changes correctly fix a real Production incident caused by the already-merged `3b80fdc`, but are not yet committed and have zero automated regression coverage. This is the single most consequential finding of the entire audit (AUD-01, treated as High/process severity) — see `REMEDIATION-PLAN.md` R-01.

Related, lower-severity finding: forcing `Request.Scheme = "https"` unconditionally for every Production request (`Program.cs:169-195`) removes the app's own ability to detect a genuinely-plain-HTTP request. Production only calls `UseHsts()`, never `UseHttpsRedirection()`, so there is no backend-enforced HTTP→HTTPS redirect — security depends entirely on Cloudflare/Render never exposing a direct plain-HTTP path to Kestrel, which cannot be verified from source (AUD-05, needs infrastructure verification). `ForwardedHeadersRegistration.cs:53-60` correctly fails startup closed if `ForwardedHeaders` is enabled in Staging/Production without `KnownProxies`/`KnownNetworks` configured — this is the one thing currently constraining who can inject `X-Forwarded-*` headers; the actual configured values were not visible from source (likely Render env vars).

## 4. Database

- **Optimistic concurrency, transactions, cascade-delete design, soft delete:** all verified sound — see Full Report §5 and Database/Redis findings below. Cascade-delete behavior was spot-checked against the ERD: `Restrict` used consistently on financially/legally significant relationships (`Transaction → Property/Payer/Receiver`, `Property → Owner`), `Cascade` only on genuinely dependent child rows (images, amenity join rows, favorites), `SetNull` on optional attribution. No accidental cascade path into financial/audit data found. Soft delete is centralized via `AppDbContext.SaveChangesAsync` intercepting hard `Remove()` calls and rewriting them — a strong, hard-to-bypass pattern.
- **Booking overlap integrity:** `UnitBookingRanges` uses a real PostgreSQL `EXCLUDE USING gist` constraint preventing overlapping date ranges at the database level — a stronger guarantee than app-layer locking alone.
- **Migrations:** the two most recently added migrations (`AddAppReleases`, `AddValuationNotificationIdempotencyAndReminderStamps`) both have non-stub `Down()` methods with real rollback logic; not exhaustively checked across all 40+ migrations.
- **Seed data:** runs unconditionally in every environment including Production (`tools/PropertyApi.Migrator/Program.cs:90`, no `IsDevelopment()` guard) — confirmed safe by content, not by environment gating: limited to reference data (currencies, governorates, plans, role names), no default admin user/password/test account, idempotent under a Postgres advisory lock. Worth knowing precisely because "seed" might otherwise be assumed dev-only.

### Database findings

| ID | Severity | File | Finding |
|---|---|---|---|
| AUD-02 | Medium | `CommonLookupService.cs` (call sites incl. lines 32, 62, 89, 117, 152, 212), `Caching/DistributedCacheExtensions.cs:10-40` | `GetOrCreateAsync` has no try/catch around Redis calls; any Redis exception propagates uncaught. All six lookup services it backs (amenities, categories, property types, cities, governorates, property-type catalog) are explicitly documented as "rebuildable data" that must "fail open" per `docs/operations/redis-failure-policy.md` — but a Redis outage currently turns these into 5xx errors instead of falling back to Postgres. `CachedAppReleaseCacheService.cs:37-67` shows the correct pattern already exists elsewhere in the codebase (its own try/catch + DB fallback), just not applied here. `docs/operations/redis-current-state-assessment.md:13` already flags this gap in the abstract; this confirms a live consumer is actually exposed to it. **Fix:** wrap `CommonLookupService`'s calls (or `DistributedCacheExtensions.GetOrCreateAsync` itself) in the same try/catch-and-fall-back-to-factory pattern. |
| AUD-06 | Low-Medium (pre-existing, corroborated) | `PersistenceInfrastructureRegistration.cs:25-32` | `UseNpgsql(connectionString)` configured with no `EnableRetryOnFailure`. Already tracked as open item F6 in `DATABASE-PRODUCTION-READINESS.md:631-641`; re-confirmed still open via repo-wide grep (zero matches for `EnableRetryOnFailure`). A transient network blip surfaces as a hard failure rather than a bounded retry. |
| AUD-07 | Low | `ValuationSlaEnforcementService.cs:284-333,~472`, `DistributionEngine.cs:113-150` | Per-item DB/notification calls inside `foreach` loops in periodic hosted-service sweeps (up to 5,000 iterations/tick in the worst case) — classic N+1 shape, but confined to background sweeps with bounded batch sizes, not user-facing request paths. The existing CI query-count gate (list ≤3, detail ≤5, geo ≤2 DB commands) only covers HTTP request paths, so this gap isn't caught by that control. **Fix:** batch-load distinct `AgencyId`s up front instead of one `GetByIdAsync` per invitation. |

## 5. Redis

- **Rate limiting fails closed (503) on Redis unavailability** (`RedisRateLimitingMiddleware.cs:106-166`) — matches documented policy exactly, genuinely implemented not just documented.
- **Security-stamp cache fails secure to the DB** on any cache error (`CachedSecurityStampValidator.cs:104-196`) — every read/write/remove path wrapped in try/catch, falls through to DB-authoritative validation rather than risk an unintended pass.
- **No Redis distributed locks anywhere** — repo-wide grep for lock-acquire patterns returned zero matches. All locking uses PostgreSQL advisory locks instead. This sidesteps the classic "lock holder crashes without TTL → deadlock" hazard entirely — a sound design choice, not a gap.
- **Redis workload isolation is key-prefix only today** (`Program.cs:96-124` — output cache, `IDistributedCache`, and rate limiting all share one connection multiplexer/instance-name prefix), exactly matching `docs/architecture/adr-redis-workload-isolation.md`'s own "proposed, implementation pending" status. The ADR is not overclaiming.
- **Lookup cache fail-open gap:** see AUD-02 above.

## 6. Performance

Not independently re-benchmarked this session (no load-testing environment available). Static review found: bounded batch sizes in background sweeps, existing CI query-count gates for HTTP request paths, and one background-sweep N+1 pattern (AUD-07) outside that gate's coverage. `docs/performance/postgresql-query-analysis.md` and the load-testing architecture docs were read and found internally consistent with the CI performance-validation workflow's actual behavior (see CI-CD-AUDIT-REPORT.md).

## 7. Security

Full detail in `FULL-CODE-AUDIT-REPORT.md` §7 and the CSRF section above. Additional items not re-litigated here (already PASS with file:line evidence in existing docs, spot-checked rather than re-derived): IDOR/BOLA ownership pattern across Properties/Bookings/ShortStay/Reviews, file-upload magic-byte/allowlist validation, password policy/lockout, rate limiting on login/register/OTP endpoints, SQL injection surface, XSS/HTML-encoding in emails. A live `dotnet list package --vulnerable` scan was not re-run this session (no network egress) — the prior audit ran it 2026-09-04 with zero results; re-running it periodically is recommended given ~2 weeks have passed.

## 8. Tests (Backend)

See `CI-CD-AUDIT-REPORT.md` Part B for full detail. Headline items:
- Zero skipped/disabled tests among 1,646 `[Fact]`/`[Theory]` cases.
- CSRF is well covered end-to-end (`CookieCsrfProtectionIntegrationTests.cs`, 7 tests covering token issuance, Partitioned/CHIPS attribute, missing/invalid-header rejection, cross-site-attacker scenario, full valid round-trip) — but **not** the new uncommitted `Program.cs` scheme-force fix (AUD-01).
- `PropertyApi.Concurrency.Tests` does not test concurrency (AUD-03) — see remediation plan.
- Test project isolation is sound: `Application.Tests` has no DB/Infra references; `Infrastructure.Tests` is pure-unit with Moq; `Integration.Tests` correctly pairs with CI's Postgres/Redis service containers, with explicit `FLUSHALL` between suites to prevent cross-suite state leakage.

## Priority-Ordered Issue List (Backend)

1. **AUD-01** (High/process) — commit and test the CSRF/scheme-force fix.
2. **AUD-02** (Medium) — lookup cache fail-open gap.
3. **AUD-03** (Medium) — mislabeled/ineffective Concurrency.Tests project.
4. **AUD-05** (Medium, needs infra verification) — Production scheme-force trust boundary.
5. **AUD-06** (Low-Medium, pre-existing) — Npgsql retry policy.
6. **AUD-07** (Low) — background sweep N+1s.
7. **AUD-12** (Low) — Staging smoke media storage replica constraint undocumented.
