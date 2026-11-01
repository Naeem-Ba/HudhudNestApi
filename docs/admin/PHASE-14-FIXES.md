# Phase 14 fixes: admin dashboard and operations

Closes the defects of the Phase 14 audit (P14-D01..D14) plus a report-visibility bug found
on the way: an administrator did not see what a reporter wrote. No schema change, no Migrator run.

## What changed

| ID | Change |
|---|---|
| D01 | An administrator cannot remove or change their own Admin role, and the last active administrator can be neither demoted nor disabled. The check runs inside a transaction under a Postgres advisory lock (`AdminRoleLockKey`), so two parallel demotions leave one admin. Refusal: 409. |
| D02 + D03 | Disabling a user also takes their listings (properties and short stays) off the public site with the fixed reason `ACCOUNT_DISABLED`. New `POST /api/admin/users/{id}/enable` (reason required, audited) restores only listings taken down with that reason. A user who deleted themselves (anonymized) cannot be re-enabled: 409. The admin user list and detail now include disabled users. |
| D04 | Approving a location suggestion validates first, runs in one transaction under an advisory lock, reuses an existing district of the same name instead of creating a duplicate, and commits once. |
| D05 | `AdminAuditFilter` writes an audit row for every mutating call on admin investments, social distribution, location suggestions, contact messages, lookups refresh, review deletion and photo deletion. Request bodies are never logged. |
| D06 | `GET /api/admin/audit-logs` (read-only, paged, filters: action, user, from, to, search). |
| D07 | IP addresses are masked when written (IPv4 last octet, IPv6 beyond /48). Retention is on by default: 365 days for admin events, 90 days for `Login` rows. |
| D08 | A huge version number in the public app-updates check returns a normal answer instead of 500. |
| D09 | Disabling an already disabled release (and enabling an enabled one) is a no-op without an audit row. |
| D10 | The `role` and `accountStatus` filters of the admin user list are validated and applied. |
| D11 | Offers: type/tier combination and free months (max 24) are validated. |
| D12 | Social: connecting needs a credential reference (max 500); approving or rejecting content that is already cancelled or published returns 409. |
| D13 | Concurrency failures return 409 instead of 400. |
| D14 / ADM-012 | No new moderation tool. An administrator may delete a single listing photo (audited), and deleting a review is audited. |
| Reports | The reporter's answer to the administrator's question notifies the assigned administrator (all administrators when unassigned) with an excerpt, via the new notification type `ListingReportReporterResponded` (63). The new-report notification carries an excerpt of the reporter's text. The report list returns `detailsPreview`. Expected business refusals are logged as `[REFUSED]` warnings, not errors. |

## Decisions taken without his answer

1. Audit-log read access is for administrators only; IP masking applies from now on and old rows keep their raw value until the retention sweep removes them.
2. Retention 365 / 90 days (sign-in rows are numerous and carry the IP).
3. Disabling hides listings (reversible) rather than deleting them.
4. No new moderation tool for photos or short-stay reviews before launch.
5. The `ACCOUNT_DISABLED` reason string is a stable code stored in `TakenDownReason`.

## Rollout

1. Merge this PR, then the app PR (the app needs the new endpoints for disable/enable and the audit screen).
2. Render deploy of the API (no Migrator).
3. Netlify release for the app.

## Left open on purpose

Phase 12 D07 (xmin concurrency token) and lead de-duplication, a retention/export UI for the audit log, and any change to the self-deletion flow.
