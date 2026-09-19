# Working prompt: finish the CI/CD gate remediation (self-brief)

Role: senior DevOps + Backend + QA engineer. Goal: get Production Gate green *for the right reasons*, keep the
original plan (Inspect -> Diagnose -> Plan -> Fix in phases -> Verify -> Report), never hide a real failure.

## Read first (before touching anything)
- `docs/operations/ci-gate-remediation-2026-09-19.md` (what is already fixed and proven)
- `docs/performance/performance-test-runbook.md`, `performance-baseline-policy.md`
- `docs/testing/staging-smoke-runbook.md`, `staging-production-gate-checklist.md`
- `docs/REDIS-HA.md`, `docs/operations/database-migration-recovery-runbook.md`
- The **real** failing run: `gh run list --commit <sha> --workflow "Production Gate"`, then
  `gh run view <id> --json jobs`, `--log-failed`, and `gh run download <id> -p "performance-*"`
  (k6 JSON, `service-logs.txt`). Evidence beats hypotheses.

## Hard rules
1. No lowering thresholds, no `continue-on-error`, no ignoring exit codes, no deleting scenarios or tests.
2. Expected results (400/401/409/429 in auth/rate-limit tests) are not failures; 5xx, timeouts, wrong bodies are.
3. A fix that changes a gate's logic must keep or strengthen what it proves, and the final evaluation stays strict.
4. Never print or commit secrets (DB URLs, keys, passwords). Credentials given in chat are used through env vars only.
   Adding GitHub secrets, touching Production, or deploying is the owner's decision.
5. Reproduce locally before claiming a fix; say plainly what could not be run (Docker was unavailable here).
6. State "fixed" only after a green re-run with evidence. Report before/after exit codes, artifacts, risks, rollback.

## Mistakes not to repeat
- Do not assume Render tracks the wrong branch: the deployed SHA matched; the cause was unapplied migrations.
- Do not treat a `Passed` sub-report as gate success: check the step exit code and the aggregate's failure list.
- Do not run a first aggregation with checks whose inputs do not exist yet (baseline comparison).
- Do not declare a root cause fixed from logs alone: my first theory (harness readiness) was incomplete; the PR run disproved it. Verify against the PR run before merging.
- Do not count a service as ready because it answered; require HTTP 200 on `/health/ready` (Redis-backed rate
  limiting fails closed with 503 when Redis is not connected).
- Do not order a smoke test so it uses a token after a probe that legitimately revokes it (refresh-token reuse
  detection revokes all sessions and rotates the security stamp).
- Shell/tooling on this machine: no `python3`; use the Edit tool for multi-line edits (nested quoting in
  `bash -c "node -e ..."` and heredocs breaks); use `git add` by path; LF files.
- The permission classifier blocks edits that look like weakening a gate even when they do not: explain the
  reason precisely and ask, do not route around it.
- PR/branch state can change under you (a PR may already exist, commits may appear): `git log`, `gh pr list` first.

## Open items after PR #163
1. `rate-limit-login`: a real product defect, not only a harness issue. The Redis IConnectionMultiplexer singleton was built lazily on the first request that needed it; with abortConnect=false it returned disconnected, so the first rate-limited requests on any fresh instance got the limiter's fail-closed 503 while /health/ready already said 200 (readiness does not touch that singleton). Waiting for HTTP 200 alone (first attempt, PR #164) did NOT fix it - PR run 35437616240 failed identically. Fix: connect eagerly before Kestrel listens with a bounded wait (Program.cs, Redis:StartupConnectTimeoutSeconds, default 15). Needs a green CI re-run.
2. Staging smoke `Refresh token rotation`: test used the new access token after a replay probe that correctly
   revoked the session family. Reordered and strengthened in `StagingSmokeJourneyTests.cs`; later journeys re-login.
3. Confirm on the next run: Recovery green, Staging migrations applied by CI (`STAGING_DATABASE_URL` present),
   full smoke journeys (property creation, upload, communication, logout) pass, evidence artifacts uploaded.
4. Only after Staging + Recovery + Performance pass: remind the owner about the Production step (not before).

## Definition of done
All mandatory jobs `success`, Production Deployment Gate `success`, staging evidence shows SHA match + migrations
complete + smoke passed, performance summary `Passed` with unchanged thresholds, and a written report with
gate table (before / root cause / fix / verification / after).
