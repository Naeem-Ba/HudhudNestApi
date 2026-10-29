# Listing reports (الإبلاغ عن الإعلانات) — developer guide

Added 2026-10-29. Lets a signed-in user report a listing and gives admins a queue to review it.
Nothing in this feature hides, deletes or restricts anything automatically.

## Rules the server enforces
- **Login required.** No anonymous reports. The reporter id always comes from the token, never from the body.
- **No self-reports:** `409 REPORT_OWN_LISTING`.
- **One open report per reporter per listing:** `409 REPORT_ALREADY_OPEN`.
- **Cooldown:** after a report from the same reporter on the same listing and reason was *closed* (resolved or dismissed), the same reason is refused for `ListingReports:SameReasonCooldownDays` (default 30): `409 REPORT_RECENTLY_REVIEWED`.
- **Limits:** 10 reports per reporter per day (`ListingReports:DailyLimitPerReporter`) plus the HTTP policy `listing-reports` (10/hour). Over the limit gives `429` (`REPORT_DAILY_LIMIT` for the daily cap). Only published, non-deleted, non-taken-down listings can be reported (404 otherwise).
- **Replay-safe submit:** the client sends one `clientRequestId` per dialog; a retry returns the first report.
- **Reason `Other` requires details** (min 10, max 2000 chars). Any reason may carry details.
- **No IDOR:** `/me/{id}` and `/me/{id}/respond` answer 404 for a report that is not the caller's. Admin routes are `Role = Admin` on the controller class.
- The listing **owner never sees reports or reporters**. The reporter sees only a collapsed status (Submitted / InReview / NeedsYourInfo / Closed) and a general outcome (ActionTaken / NoViolationFound). Internal notes, admin names and the exact action are never in a reporter DTO.

## Data model (migration `AddListingReports`)
- `ListingReports`: sequence-backed `Number` (shown as `RPT-000042`), `PropertyId`, snapshot of the title at report time, `ReporterUserId`, `Reason`, `Details`, `Status`, `Priority`, `AssignedAdminId`, `Outcome`, `ResolutionNote` (internal), `InfoRequest`, `ReopenCount`, timestamps, `xmin` row version. Partial unique index: one open report per (reporter, listing). Check constraints keep the enums valid.
- `ListingReportEvents`: append-only audit timeline (`Created, StatusChanged, PriorityChanged, Assigned, Unassigned, NoteAdded, InfoRequested, ReporterResponded, ListingTakenDown, ListingRestored, Closed, Reopened`), each with actor, from/to, note and `IsInternal`.
- If the listing is later soft-deleted the report stays readable (`listing = null` in the admin DTO; the title snapshot is kept).

## Status workflow
`New → UnderReview → (WaitingForInformation | ActionRequired) → Resolved | Dismissed`.
There is no separate "Reopened" status: reopening returns the report to `UnderReview`, increments `ReopenCount` and writes a `Reopened` event. The **outcome** chosen on close decides Resolved vs Dismissed (`NoViolation`, `InsufficientEvidence`, `DuplicateReport` dismiss; the others resolve). Closing needs an outcome and a note, and **never touches the listing**.

## Priority
Default priority comes from the reason (`ListingReportPolicy.DefaultPriority`: fraud, fake listing, impersonation, off-platform payment, illegal/offensive content = High). When 3 or more *different* accounts have open reports on one listing, open reports on it are raised to High. That is a queue signal only (coordinated reporting against a competitor is possible), shown to admins as a banner. Review targets (hours): Urgent 24, High 48, Normal 120, Low 240 (`ListingReports:ReviewTargetHours*`); a report past its target is "overdue".

## API
User (`api/listing-reports`): `GET reasons`, `POST` (rate limited), `GET me`, `GET me/open?propertyId=`, `GET me/{id}`, `POST me/{id}/respond` (rate limited).
Admin (`api/admin/listing-reports`): `GET` (filters: search, status, open, reason, priority, assignedAdminId, unassigned, propertyId, from, to, overdue, sort, page, pageSize), `GET stats`, `GET assignees`, `GET {id}`, and `POST {id}/` `start-review | assign | priority | notes | request-info | action-required | close | reopen | take-down-listing | restore-listing`. Bodies carry only the decision's own inputs; the server derives status and actor.
Take-down/restore reuse the existing reversible admin take-down of a listing (reason shown to the owner) and are also written to the report timeline and the audit log.

## Notifications
Types `ListingReportSubmitted` (to admins), `ListingReportInfoRequested` and `ListingReportClosed` (to the reporter), in ar/de/en in the recipient's language, with a dedupe key. They are registered in `NotificationKindCatalog` as mandatory `account` kinds. Sending is best-effort: a failure is logged and never fails or rolls back the report. Texts never include the reporter's identity (admin side shows only the reference) or internal notes.

## Attachments / evidence — deferred
Not built. Safe evidence upload needs private, non-public storage with signed short-lived URLs, content-type sniffing, size and count limits, malware scanning, and EXIF stripping; the project has no such private store today (listing photos are public). Until it exists, the report accepts text only. To add later: a private bucket, an upload endpoint bound to a report id and its reporter, and admin-only read URLs.

## Tests
`tests/HudhudNestApi.Integration.Tests/Moderation/ListingReportTests.cs` (submit, duplicate, own listing, IDOR, rate limit, admin workflow, take-down separation, reporter DTO privacy, replay) and `tests/HudhudNestApi.Application.Tests/Moderation/ListingReportDomainTests.cs` (state machine). App side: specs next to the pages and the report button.

## Rollout (manual, in this order)
1. Merge API PR, run the Migrator (`AddListingReports`).
2. Render deploy of the API.
3. Merge app PR, then main → release and the Netlify publish.
The app tolerates an old API: the button's calls fail softly and the form shows a generic error.
