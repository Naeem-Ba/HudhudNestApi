# Frontend Audit Report — PropertyApi

## Scope note: no frontend code exists in this repository

`C:\Users\naeem\Source\Repos\PropertyApi` contains **only the backend** (.NET 8 Clean Architecture: `PropertyApi.Domain`, `PropertyApi.Application`, `PropertyApi.Infrastructure`, `PropertyApi` API/composition root, plus `tests/`, `tools/`, `ci/`, `scripts/`, `docs/`, `.github/workflows/`). There is no Angular/React/TypeScript project, no `package.json` for a client app, and no `wwwroot` content beyond what the API itself serves (Swagger UI assets).

Per prior session context, the Angular single-page application that serves as this project's frontend lives in a **separate repository at a separate path**, outside this working directory. Auditing it was out of scope for this run because:

1. This audit's file-system access is scoped to `C:\Users\naeem\Source\Repos\PropertyApi`.
2. The scheduled audit task's instructions describe a generic dual-repo (backend + frontend) review checklist, but do not supply the frontend repository's path or grant access to it.

**No frontend findings are reported here** — fabricating them against code this audit never read would be worse than reporting nothing. This is a scope gap, not a "frontend passed" result.

## What would be needed to complete this section

To produce a genuine `FRONTEND-AUDIT-REPORT.md` covering the checklist in the task brief (component structure, routing/guards, state management, forms, API integration, XSS/DomSanitizer usage, token storage, bundle size, accessibility, frontend test quality), a future run needs:

- The frontend repository's local path or clone URL, with read access granted.
- Confirmation of which frontend build/deploy pipeline (if any) is in scope, since this repo's `.github/workflows/` only cover the backend.

## Cross-repo items worth checking once frontend access is available

These surfaced during the backend audit and have a frontend-side counterpart worth verifying against the Angular app when it's in scope:

- **CSRF token handling**: this backend audit found the CSRF cookie/header flow is mid-fix on the current branch (see `FULL-CODE-AUDIT-REPORT.md` §7). Confirm the Angular app's HTTP interceptor sends the `X-XSRF-TOKEN` header on every mutating request and handles a `403 CSRF_VALIDATION_FAILED` response gracefully (e.g. re-fetch token and retry once, rather than silently failing).
- **Partitioned (CHIPS) cookie attribute**: backend cookies (`refresh_token`, CSRF) are marked `Partitioned` per commit `0043b2a` — confirm the frontend's cross-site embedding scenarios (if any) still function correctly under CHIPS-partitioned storage in the browsers the project targets.
- **Rate limiting (503 fail-closed)**: `RedisRateLimitingMiddleware` returns 503 when Redis is unavailable rather than allowing traffic through — confirm the frontend has a sane retry/backoff and user-facing message for 503s distinct from its handling of 4xx validation errors.
- **CORS allowlist**: confirmed backend-side to be an explicit allowlist-or-throw pattern — confirm the frontend's configured API origin(s) per environment (local/staging/production) match what's actually allowlisted server-side, consistent with prior session notes about a staging Netlify build once silently building as production due to environment misconfiguration.
