# Notification system: foundation (priority, dedupe, preferences, retention)

> Built 2026-10-17. Audit and event matrix: project folder `notifications/01-audit-and-matrix-ar.md`.
> Builds on `preferences-notifications-push.md` (language, device tokens, FCM).

## What changed

| Area | Change |
|---|---|
| `Notifications` table | `Priority` (Low/Normal/High, default Normal) and `DedupeKey` (nullable) + partial unique index `(RecipientId, DedupeKey)`. |
| Idempotency | `INotificationRepository.AddAsync` returns `false` when the key already exists for that recipient; the service then sends neither the live (SignalR) nor the device push. Keys: `msg:{messageId}`, `ssm:{propertyId}` (saved search), `ami:{messageId}`, `agi:{invitationId}`, `val-*:{id}`. |
| Type catalog | `NotificationKindCatalog`: for every `NotificationType` its category, priority, target screen and whether it is mandatory. A test fails when a type has no row. |
| API response | `GET /api/notifications` items now also carry `category`, `priority`, `target {kind,id,propertyId}`. Additive. |
| Preferences | `GET/PUT /api/notifications/preferences` and table `NotificationPreferences`: muted categories (nothing stored), push-muted categories (stored, no ring), push preview (`Generic` default, `Full`), quiet hours + IANA time zone. Mandatory events ignore mutes and quiet hours; quiet hours hold back only Low/Normal pushes. |
| Push text | Default preview is a name-free sentence per category in the recipient's language (`push.generic.*` in `NotificationCatalog`). The in-app text is unchanged. FCM `data` now carries `targetKind` and `category` so a tap opens the right screen. |
| Read sync | Marking read (REST or hub) broadcasts `UnreadCountChanged` and `NotificationsRead` to the user's whole group (other tabs and devices). |
| Retention | `NotificationRetentionHostedService` (daily, advisory lock): read notifications older than 180 days and removed ones older than 30 days are deleted; unread are kept. Config `Notifications:Retention:ReadDays` / `DeletedDays` (floors 30 / 1). |
| Saved search | A sweep notifies the oldest 10 matches and advances its cursor only to the last one handled, so matches beyond the cap are no longer lost; the dedupe key stops repeats if a sweep is retried. |

## Migrations (both additive)

* `AddNotificationPriorityAndDedupe`: two columns + one filtered unique index on `Notifications`.
* `AddNotificationPreferences`: one new table.

Rollback: the application rollback leaves the columns and table unused. `Down` drops them.

## Not in this change

Outbox / retried delivery, per-device delivery status, Web Push, quiet-hours deferral (held-back pushes are not re-sent later), and the missing event hooks (plans and payments, support, security alerts on mobile). These are the next phases in the plan.

## Rollout (owner's manual steps)

1. Merge this PR. 2. Run the Migrator (two migrations). 3. Render deploy. 4. Then merge the app PR, main -> release, Netlify publish. The API is backward compatible with the current app (new fields are ignored); the app is forward compatible with the old API (it falls back when the fields are absent).
