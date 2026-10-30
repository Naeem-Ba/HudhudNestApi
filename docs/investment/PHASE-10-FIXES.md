# Investment module: QA Phase 10 fixes (2026-10-29)

Audit report: `qa-plan/phase-10-investments.md` (project folder). This file records what was changed and why, the
choices made where the owner had not decided, and what is deliberately **not** solved.

Nothing here deploys itself. No schema change: **no migration** (the notification type is stored as a number, the
interest count is computed). Rollout: merge, Render deploy; then the app.

## What changed

| ID | Problem found | Fix | Where |
|---|---|---|---|
| D01 (P1) | An expression of interest reached nobody: no admin endpoint, screen or notification | `GET /api/admin/investments/projects/{id}/interests` (name, email, phone, status, dates); `activeInterestCount` in the admin list; admins get a notification (`InvestmentInterestReceived`, never names the person); a section in the admin project screen | `GetAdminInvestmentInterests`, `InvestmentInterestNotifier`, `AdminInvestmentsController` |
| D02 (P2) | Documents marked "private" are stored as public Cloudinary assets | **Not solved in code** (see Limits). The upload screen now says "private = not listed, not secret" | app: `DOCUMENT_PRIVACY_WARNING` |
| D03 (P2) | `ScheduledPublishAt` was ignored; manual publish worked before it | `InvestmentScheduledPublishHostedService` (every 5 min, `BackgroundJobLock`) publishes due projects through the normal publish command, so the readiness rules apply at that moment. A manual publish before the date is refused (400). With no date, publishing stays manual | `InvestmentProject.Publish`, hosted service |
| D04 (P2) | A suspended project could never come back | `InvestmentProject.Resume()`, `POST .../resume` (Suspended to Published), a Resume button | domain, `ResumeInvestmentProject` |
| D05 (P3) | A suspended project stayed in the user's watchlist/interests | Both lists show only publicly visible projects (Published, property alive); the rows are kept, so the project returns when re-published | `InvestmentWatchlistRepository`, `InvestmentInterestRepository` |
| D06 (P2) | NUL in search/city gave 500; `%` and `_` acted as wildcards | NUL rejected with 422 (public and admin); LIKE terms escaped | validators, `InvestmentProjectRepository.EscapeLike` |
| D07 (P2) | Anonymous calculator: 500 on huge amounts, no caps | amount at most 1e12, term at most 600 months (422); the app mirrors the caps | `CalculateInvestmentReturnQueryValidator` |
| D08 (P3) | Parallel watchlist/interest add gave 500 | The two unique indexes map to 409 | `UnitOfWork.UniqueViolationConflictMessages` |
| D09 (P3) | Weak admin validation | Title/short description/description/risk summary/reasons: no NUL, guaranteed-return wording refused (also in the title); currency must be supported; minimum <= target; term 1..600; return <= 100%; money <= 1e12. The same limits are in the domain | validators, `InvestmentProject.UpdateInvestmentParameters` |
| D10 (P3) | The last required public document could be removed after publish | `RequiredDocumentGuard`: blocks deleting/hiding the last public ProjectPlan/FinancialStatement of a Scheduled/Published/Suspended project (upload a replacement first). A closed project accepts no new updates | handlers |
| D11 (P3) | Documents/updates/risk stayed public after the property was deleted | The three public queries join the property like the details/list queries do | repositories |
| D12 (P3) | The raised amount froze after publish | `PUT .../progress {raisedAmount}` for Approved/Scheduled/Published/Suspended (0 to target). Still informational, never from a payment | `UpdateInvestmentProgress` |
| D13 (P3) | The button said "I want to invest"; no general disclaimer | Button: "Express my interest" (ar/en/de); a general disclaimer on the list and at the top of the project page | app i18n |

## Choices made without a ruling from the owner

These are defaults; each is one small change to reverse.

- **D01**: admins are told in-app (and by push where the existing channel does); no e-mail. The contact details on the
  list are the account's e-mail and phone, visible to admins only.
- **D03**: a project with a date publishes itself; a project without one stays manual. A due project that is no longer
  ready stays Scheduled and is retried every 5 minutes (a warning is logged each time).
- **D04**: resume goes straight to Published. It does not re-run the readiness check, because D10 now keeps the
  required documents in place and the financials/risk cannot be deleted.
- **D05**: withdrawn/closed projects also leave the lists. A user who watched a project that later closed no longer sees it.
- **D09**: the bare word "guaranteed" is not banned (honest disclaimers use it); only claims such as "sure profit",
  "capital is safe", "ربح مؤكد" are.

## Limits (not solved, not measured)

- **D02 is open.** A document the admin marks "private" is only hidden from the project page; the Cloudinary URL is
  public to anyone who has it. Real protection needs signed/authenticated delivery, which cannot be built or tested
  without live Cloudinary. Until then: do not upload sensitive documents (IDs, signed contracts, bank details).
- Not measured: real Cloudinary, push delivery of the new notification on a device, the screens in a browser or on a
  phone, and the production database. Legal review of the disclaimer wording is still open (see the app repo's
  `docs/legal/LEGAL-REVIEW-REQUIRED.md`, item 10).
- The scheduler's timing was tested by moving the stored time into the past (the clock is not faked in the domain).

## Tests

`tests/HudhudNestApi.Integration.Tests/Investments/InvestmentPhase10FixTests.cs` (35 tests, real HTTP and Postgres),
`InvestmentInterestNotifierTests`, the changed unit tests for the handlers, the new lock key in
`AdvisoryLockConcurrencyTests` and `BackgroundJobLockExclusionTests`.
