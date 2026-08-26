# Email delivery and address confirmation

> Built 2026-08-24 (`32905c5`) and 2026-08-25 (`293a5c1`). Verified at `293a5c1`.
> Related: `docs/password-policy.md` · `docs/user-secrets.md` ·
> `docs/architecture/authentication-orchestration.md`.

## What this subsystem is for

Registration by email mints an ASP.NET Identity confirmation token and mails a link to the
address the user typed. Confirming that link sets `EmailConfirmed`.

**Login is not gated on confirmation.** An unconfirmed account signs in normally. This is a
deliberate product choice and it shapes every decision below: a confirmation that fails to
arrive is a degraded experience, not a locked-out user, so nothing in the send path is
allowed to fail a registration.

## Provider selection

`Email:Provider` picks the sender at composition time. `AddEmailServices`
(`Infrastructure/Auth/AuthInfrastructureRegistration.cs`) publishes exactly one
implementation under **both** contracts — `IApplicationEmailSender` (the application's own)
and `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender` (Identity's).

| Value | Class | Behaviour |
|---|---|---|
| `Resend` | `ResendEmailSender` | HTTP API, `POST {BaseUrl}/emails` |
| `Smtp` | `SmtpEmailSender` | SMTP conversation |
| `Console` | `ConsoleEmailSender` | Writes to the log. **Refused in Production** |
| *(blank)* | — | Falls back to the old rule: SMTP in Production, console elsewhere |

Two rules are enforced at **startup**, which is the only place they can still stop a
deployment:

1. **`Console` in Production throws.** It only logs. Reaching Production with it selected
   would mean nobody ever receives a link, and nothing would say so until someone
   complained.
2. **A blank `Email:Provider` preserves prior behaviour**, so an environment provisioned
   before this setting existed keeps working exactly as it did.

The aliases are registered `Scoped` even when the sender behind them is not: `AddHttpClient`
registers its typed client `Transient`, and `CompositionRegistrationTests` pins
`IApplicationEmailSender` to a scoped lifetime. Resolving through factory delegates keeps
that promise regardless of the concrete type's lifetime.

## Resend

`ResendEmailSender` posts JSON with a Bearer key. Three decisions worth preserving:

| Decision | Reason |
|---|---|
| `HttpClient` comes from `IHttpClientFactory` | `HttpClientLifetimeTests` fails the build on `new HttpClient`; the pooled handler keeps DNS changes visible and sockets bounded |
| The API key is set **per request**, not on `DefaultRequestHeaders` | the typed client is configured once at startup; reading the key at send time means a rotated secret takes effect without a restart |
| No retry policy | the solution carries no Polly, and both callers already treat a failed send as non-fatal and log it |

Failure bodies are surfaced in the log because Resend's own `{name, message}` is how
"domain not verified" and "you may only send to your own address" are told apart. The body
never contains the API key — that lives only in the header.

### Startup validation

When the provider is `Resend`, all of these are `ValidateOnStart`:

- `Email:Resend:ApiKey` — non-empty
- `Email:Resend:BaseUrl` — absolute **HTTPS** URL
- `Email:Resend:TimeoutSeconds` — 1 to 120
- `Email:From` — non-empty

Regardless of provider, `ProductionStartupValidator` additionally runs
`EmailOptions.ValidateForEnvironment` in Production: `Email:From` must not still be the
shipped placeholder domain (see below).

A misconfigured deployment therefore fails at boot rather than at the first registration.

### The one placeholder startup validation now catches — and the one it still can't

`Email:From` defaults to `no-reply@propertyapi.local`, a non-routable domain. Resend rejects
unverified sending domains with a 403 — and **both callers swallow send failures** (see
below). A deployment that kept the default used to pass every startup check, register users
normally, and deliver zero mail, with the only trace an ERROR line nobody was watching.

`EmailOptions.ValidateForEnvironment`, called from `ProductionStartupValidator` alongside the
SMS check it mirrors, now refuses to start in Production if `Email:From` still ends in
`@propertyapi.local` — the exact shipped default. That closes the specific mistake of
forgetting to change it.

**It cannot close the general case.** A real domain that exists but was never verified in the
Resend dashboard passes this check, passes every other startup check, and still gets a silent
403 at send time — only Resend's own dashboard knows a domain's verification state, and
nothing in this process can ask it at boot. **Set `Email:From` to an address on a domain
verified in the Resend dashboard, per environment**, and confirm it with a real send in
Staging (see `docs/testing/staging-production-gate-checklist.md`) — this remains a
release-checklist item, not something code alone can guarantee.

## The confirmation flow

```
RegisterCommandHandler
  └─ CommitTransactionAsync                  ← the account exists first, always
     └─ SendConfirmationEmailAsync           ← try/catch, never rethrows
        ├─ GenerateEmailConfirmationTokenAsync(identityId, CancellationToken.None)
        └─ IEmailVerificationService.SendVerificationLinkAsync(email, token)
           ├─ UserManager.FindByEmailAsync   ← resolves the identity id for the link
           ├─ IEmailConfirmationUrlBuilder.Build(identityId, token)
           ├─ EmailConfirmationTemplate.BuildHtml / BuildText
           └─ IApplicationEmailSender.SendEmailAsync
```

### Why the send is after the commit, and swallows everything

By the time it runs, the registration is committed: the account exists and the user can sign
in. Rethrowing would report a sign-up that succeeded as one that failed, and send the user
back to register against an email address that is now taken — a worse outcome than a missing
message.

### Why the request's CancellationToken is not threaded through

It is cancelled when the client goes away. A user who closes the tab the instant after
submitting has still registered and still needs the link. The same reasoning already governs
the rollback path in the same handler.

## Resending

`POST /api/auth/email/resend-confirmation` covers the lost-mail case.

- **Anonymous by necessity.** The caller is someone who cannot finish signing up because the
  first link never arrived; gating this on a token would exclude exactly the accounts that
  need it.
- **Rate-limited** under the `auth-password-reset` policy.
- **Identical response** whether the address is unknown, already confirmed, or was just sent
  to — otherwise the endpoint enumerates accounts.
- **The send failure is swallowed for that same reason:** letting a provider outage become a
  500 would make the status code answer "does this account exist?", which is the one question
  the identical responses exist to refuse.

⚠️ **Nothing in the Angular app calls this endpoint yet.** See `B-15` in the frontend
repository's `docs/BACKEND-ISSUES.md`.

## The confirmation URL

`EmailConfirmationUrlBuilder` replaced inline code inside `EmailVerificationService` that
read `Frontend:BaseUrl` with a hard-coded `http://localhost:4200` fallback — and **neither
that key nor its alternate existed in any appsettings file**, so every confirmation link ever
sent, Production included, pointed at localhost, silently.

Three rules it now enforces:

1. **The origin comes from configuration, never from the request's `Host` header.** A proxy
   forwarding an attacker-controlled host would otherwise mint valid confirmation tokens
   pointing at the attacker's site.
2. **Production requires `Frontend:BaseUrl`, absolute and HTTPS**, or the application refuses
   to start. `ValidateConfirmationLinkOrigin` asks the builder its own question at startup —
   built rather than re-checked, so there is one set of rules and not two.
3. **The recipient's address was dropped from the query string.** Neither the verify endpoint
   nor the frontend page reads it, so it was personal data in a URL for no purpose.

### ✅ Fixed (was 🔴): the path used to be wrong

The Angular app runs on `provideRouter(routes, withHashLocation())`. Routes only exist at
`{baseUrl}/#/...`. The builder used to emit `{baseUrl}/auth/verify-email?userId={id}&token=
{token}` — without the `#`, the query string sat outside the hash, `ActivatedRoute.
queryParamMap` never saw `userId` or `token`, and the router fell through
`{ path: '**', redirectTo: 'home' }`. **Every confirmation link landed on the home page.**
`Frontend:PasswordResetUrl` carried the same defect and had for longer: the default shipped in
`appsettings.json` was `http://localhost:4200/reset-password`, missing the `#` *and* using a
path that was never registered (the route is `auth/reset-password`).

Neither side's tests could catch this. `EmailConfirmationUrlBuilderTests` verified what the
backend promises itself — absolute URL, HTTPS in Production, encoded token — and the Angular
page verified that it reads `queryParamMap`. The contract that broke was the one no test
owned.

**Fix applied (Option B from the release-blockers report):** `EmailConfirmationUrlBuilder`'s
`ConfirmationPath` now includes the `#/` prefix, `PasswordResetUrlBuilder`'s development
fallback was corrected to the registered `#/auth/reset-password` path, and
`Frontend:PasswordResetUrl` was corrected in `appsettings.json`,
`appsettings.Development.example.json`, and `.env.example`. `PasswordResetUrlBuilderTests`
was added — the class had no test coverage at all before this fix.

**Still open, by design:** dropping hash routing and adding an SPA fallback rule in hosting
(Option A) remains the better long-term answer — it also fixes link sharing and SEO — but was
deliberately deferred rather than mixed into the same release as this fix. **Every deployed
environment's `Frontend:PasswordResetUrl` still needs updating outside this repo**, and this
still is not closed by a unit test: it requires an actual click on a link delivered to a real
inbox in Staging, for both the confirmation and reset-password flows. Tracked as **B-14**.

## Configuration keys

| Key | Required when | Notes |
|---|---|---|
| `Email:Provider` | always, in practice | `Console` \| `Smtp` \| `Resend`; blank = legacy rule |
| `Email:From` | Resend or SMTP | **must be on a verified domain** |
| `Email:FromName` | optional | display name beside `From` |
| `Email:Resend:ApiKey` | Provider = Resend | never committed — user-secrets locally, `Email__Resend__ApiKey` when deployed |
| `Email:Resend:BaseUrl` | optional | defaults to `https://api.resend.com` |
| `Email:Resend:TimeoutSeconds` | optional | defaults to 10; 1–120 |
| `Frontend:BaseUrl` | **Production** | absolute HTTPS; boot fails without it |
| `Frontend:PasswordResetUrl` | Production | must include the `#/` hash-routing prefix (see B-14 above) |

Local setup: `docs/user-secrets.md`.
