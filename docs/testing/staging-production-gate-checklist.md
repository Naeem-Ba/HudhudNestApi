# Staging and Production gate checklist

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
