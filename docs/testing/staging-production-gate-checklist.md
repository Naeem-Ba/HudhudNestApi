# Staging and Production gate checklist

## Live configuration status (checked 2026-09-01 via `gh api repos/Naeem-Ba/HudhudNestApi/environments/staging/...`)

The GitHub Environment `staging` **exists** (created 2026-07-12, no protection rules, no branch
restriction) but currently has **zero Variables and zero Secrets** configured on it. A stray
*repository-level* `STAGING_BASE_URL` variable (`https://staging-api.example.com`, a leftover
placeholder from initial repo setup) was found and removed on 2026-09-01, because repo-level
variables leak into every environment/workflow and are not what `docs/testing/staging-smoke-runbook.md`
specifies -- values must live on the `staging` **environment**, not the repository root. Its presence
was also the reason the workflow's failure mode changed from `STAGING_BASE_URL is required` to
`PRODUCTION_BASE_URL is required`: `require_value` checks `STAGING_BASE_URL` first, and the stray
value made that specific check pass.

**Update (2026-09-04): resolved.** The `staging` GitHub Environment now has all required Variables
(`STAGING_BASE_URL`, `PRODUCTION_BASE_URL`, `STAGING_SMOKE_PHONE_PREFIX`, `STAGING_PROMETHEUS_URL`,
`STAGING_TEMPO_URL`) and Secrets (`STAGING_DEPLOY_HOOK_URL`, `STAGING_SMOKE_PASSWORD`,
`STAGING_SMOKE_FIXED_OTP`, `STAGING_SMOKE_CLEANUP_SECRET`) configured, and a real Staging deployment
exists on Render (`propertyapi-staging-api` + dedicated Postgres/Redis) -- see [[render-staging-deployment]]
memory for the live resource names. `scripts/verify-observability.sh` passes end-to-end against it.

Separately: the `deploy-production` job in `production-gate.yml` declares `environment: production`.
That GitHub Environment now exists (auto-created 2026-09-04 the first time `deploy-production` ran off
a `master` push), but **attempting to configure "Required reviewers" protection on it fails**: GitHub
returns `Please ensure the billing plan supports the required reviewers protection rule` for this
private repository under the current org billing plan. Until that plan is upgraded (or reviewer
protection is otherwise made available), `deploy-production`'s trigger was narrowed to
`github.event_name == 'workflow_dispatch'` only (2026-09-04) -- it no longer also accepts a plain push
to `main`/`master` -- so an actual Production deploy always requires someone to deliberately run the
workflow by hand, rather than firing automatically off a merge with zero human gate. This is a
mitigation, not equivalent to real required-reviewer protection: anyone with push access can still run
`workflow_dispatch` alone. Revisit if/when the billing plan changes.

## Infrastructure

- [ ] Stable external HTTPS Staging URL exists.
- [ ] Staging service is distinct from Production.
- [ ] Dedicated PostgreSQL/PostGIS database name contains the Staging marker.
- [ ] Dedicated Redis resource or namespace is configured.
- [ ] Dedicated image-storage account/folder is configured.
- [ ] Staging secrets are distinct from Production.
- [ ] Real SMS, email, and webhooks are disabled.
- [ ] `Deployment__CommitSha` is injected from the deployed release.

## GitHub environments

- [ ] All documented Staging variables and secrets exist.
- [ ] Production environment requires reviewer approval.
- [ ] `PRODUCTION_DEPLOY_HOOK_URL` exists only in the Production environment.
- [ ] Concurrency prevents overlapping release gates for the same ref.

## Mandatory verification

- [ ] Missing/empty/invalid Staging URL fails.
- [ ] Production hostname supplied as Staging fails.
- [ ] Missing deploy hook, OTP, phone prefix, password, or cleanup secret fails.
- [ ] Staging deployment is triggered for the intended commit.
- [ ] Environment is `Staging` and commit SHA matches.
- [ ] Readiness, EF migrations, and PostGIS pass.
- [ ] Registration, OTP, login, refresh rotation, and logout pass.
- [ ] Property create and immediate owner read pass.
- [ ] Image upload, persistence, and reachable image pass.
- [ ] Publish, public detail, and listing discovery pass.
- [ ] Messaging persistence and authorization pass.
- [ ] Negative authentication, authorization, validation, and token tests pass.
- [ ] Exact-scope cleanup succeeds.
- [ ] JSON, JUnit, and Markdown reports are uploaded.
- [ ] No mandatory journey is skipped.

## Email configuration

- [ ] `Email:Provider` is `Resend`.
- [ ] `Email:From` is an address on a domain verified in the Resend dashboard.
- [ ] `Email__Resend__ApiKey` is a real key from an environment variable, not a file.
- [ ] `Frontend:BaseUrl` is the deployed frontend origin, HTTPS.
- [ ] `Frontend:PasswordResetUrl` includes the `#/` hash-routing prefix (see
      `docs/architecture/email-confirmation-and-delivery.md`, B-14).
- [ ] `Cors:AllowedOrigins` includes the deployed frontend origin.
- [ ] A real account was registered in Staging and the confirmation email was confirmed
      to arrive in an external inbox -- not just a "Resend accepted" log line.

## Release decision

Production may be approved only when the real provider-hosted Staging job is successful. A local Docker
pass is necessary evidence but is not sufficient to declare Production readiness.

## Update (2026-09-30): environment separation audit

See [environments-and-release-flow.md](../operations/environments-and-release-flow.md) for the full
Staging/Production map, release steps, known gaps and the audit's live findings. Changes made:
`deploy-production` now also requires `github.ref == 'refs/heads/master'` and refuses to run when
`master` moved after the gated run started (the deploy hook ships the branch tip, not `github.sha`);
Production now refuses to boot with Staging-shaped configuration (`ProductionEnvironmentGuard`);
Staging uploads default to a `staging/` Cloudinary folder root; `GET /api/operational/version`
(Admin) reports environment, version and commit in every environment.
