# Phase 13 fixes: plans, listing fees, subscriptions

Settlement is manual (no payment gateway): a paid plan or a listing fee is only a
*request*, and an administrator confirms the money arrived. This change closes the
defects found in the Phase 13 audit (P13-D01..D09) without touching that model.

## What changed

| ID | Change |
|---|---|
| D01 | `Property.CanBeFeatured` (exists, published, not taken down, not sold/rented/archived/expired). Used when a featured fee is requested (409) and when an administrator confirms it (409, the fee stays Pending). Direct admin feature is unchanged. |
| D02 | `GET /api/admin/payment-requests` now returns `extensions`. New `POST extensions/{id}/approve` and `POST extensions/{id}/reject`. Rejecting works for both fee kinds (status Failed, reason kept, payer notified). |
| D03 | Six notification types (plan request approved/rejected, listing fee confirmed/rejected, plan expiring soon, plan ended) in ar/en/de, sent after commit by `BillingNotifier`, which never throws. `PlanExpiryNoticeHostedService` sweeps every 6 h (5 days before the end, and for plans that ended in the last 7 days), de-duplicated per end date. `GET /api/users/me` returns `planStatus` and `planExpiresAt`. |
| D04 | Choosing the free plan while a paid plan is in force returns 409 with code `PAID_PLAN_ACTIVE`. Only an administrator cancels a paid plan. |
| D06 | A direct admin grant and the free selection close (Superseded) the user's pending plan request inside the same advisory lock. |
| D07 | `AdminReasonAttribute`: at most 500 characters, no NUL, on every admin "reason" field. |
| D08 | Cancelling an already cancelled plan and un-featuring a listing that is not featured return 409. |

D05 and D09 are app-only (see the app PR).

## No schema change

`NotificationType` is stored as an integer; the new members are 57..62. No migration, no
Migrator run. The new hosted service needs no configuration.

## Rollout

1. Merge this PR, then the app PR (the app tolerates the old API, but the new queue section and
   `PAID_PLAN_ACTIVE` text need the new endpoints).
2. Render deploy of the API.
3. Netlify release for the app.

## Left open on purpose

The bank reference wording, a subscriber detail page with full history, and any payment gateway.
