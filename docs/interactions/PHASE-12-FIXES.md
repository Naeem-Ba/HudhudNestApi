# Interactions (favorites, saved searches, visits, messages, public forms): QA Phase 12 fixes (2026-10-30)

Audit report: `qa-plan/phase-12-interactions.md` (project folder). This file records what was changed, the
choices made without a ruling from the owner, and what is deliberately **not** solved.

Nothing here deploys itself. **No migration, no schema change** (the one model change is a query filter, which is not
stored in the schema). Rollout: merge, Render deploy.

## What changed

| ID | Problem found | Fix | Where |
|---|---|---|---|
| D01 (P2) | Creating a saved search answered 500 for a name or city of 5000 characters, a country code that is not two letters, and an unknown or negative governorate, district, neighborhood or property type | Texts at most 150 and without NUL, locations must exist and agree (`ILocationHierarchyChecker`), property type must exist, listing type must be a known value (422) | `CreateSavedSearchCommandValidator` |
| D02 (P2) | A user could keep an unbounded number of saved searches (every one is scanned by the background matcher and can notify), and nonsense ranges were accepted | At most 20 per user (409 with a clear message), prices and areas not negative and inside the column range, rooms 0 to 50, minimum not above maximum (422), `user-write` rate limit on the create endpoint | `CreateSavedSearchCommandHandler`, validator, `SavedSearchController` |
| D03 (P2) | The owner's note on confirm, decline and propose-alternate had no length limit; 100000 characters overflowed the varchar(500) column and answered 500 | At most 500 characters and no NUL (422), the same limit the visitor's note already had | validators next to the three commands |
| D04 (P2) | Favorites accepted another user's unpublished listing and then showed its title, city, price and photo, although `GET /api/properties/{id}` answers 404 for it; a listing unpublished after it was saved stayed visible; messaging a draft was accepted and notified its owner | Adding a favorite and sending a message need a listing that is published or that the caller owns (404 otherwise); the favorites list hides listings that are neither | `AddFavoriteCommandHandler`, `FavoriteRepository`, `SendMessageCommandHandler` |
| D06 (P3) | Error messages of the review validator, delete-review and property-visits handlers reached the user as mojibake | Strings restored to proper Arabic | three files |
| D10 (P3) | The contact subject accepted line breaks; the lead phone accepted any text of six characters; the survey stored enum values that do not exist | Subject must be one line (422); phone follows the landing form's own rule (separators ignored, then 7 to 15 digits, optional `+` or `00`); survey enums must be known values | validators next to the three commands |
| D05 (P3, owner's decision 2026-10-30) | The owner could complete a visit before its date (which opens reviews for the listing), and a visit could be requested on a sold listing | Completing a confirmed visit before its date answers 400; requesting a visit on a Sold or Rented listing answers 400 (checked before the owner check, so the owner of a closed listing is also refused) | `CompleteVisitCommandHandler`, `RequestVisitCommandHandler`, `PropertySummary.Status` |
| Admin alert (P3, owner's decision 2026-10-30) | A contact-form message waited in the admin list until somebody opened it | Every active admin gets a `ContactMessageReceived` notification (sender name and subject; the text and e-mail stay in the list), one per message (dedupe key `cmr:{id}`), sent after the commit and never able to fail the submit | `ContactMessageNotifier`, `NotificationService`, enum value 56 (no migration) |
| D11 (P2, found while testing D02) | Deleting a saved search is a soft delete (SaveChanges turns `Remove` into `IsDeleted = true`), but `SavedSearch` had no query filter. The deleted search stayed in `GET /api/savedsearch`, was still matched by the background job and still notified its owner | Global filter `!IsDeleted` on `SavedSearch`, like the other soft-deleted entities (and like the Phase 9 fix for pricing rules) | `AppDbContext` |

## Choices made without a ruling from the owner

- **Cap of 20 saved searches per user.** A constant (`CreateSavedSearchCommandHandler.MaxSavedSearchesPerUser`), easy to change.
- **Lead phone rule copied from the landing page**, so nobody who can submit the form is rejected by the server.
- **A survey with no answers stays valid.** The audit listed it as a defect, but `SurveyResponse` documents it as a signal ("opened it and submitted without answering"), so it is left alone.
- **Rented is treated like Sold** for the visit block (`Property.IsClosedDeal`). Archived and expired listings are not added: an unpublished listing is already refused by the existing published check.
- **The completion block only applies to a confirmed visit.** The domain method `Complete()` is untouched (existing domain tests call it on future visits); the rule lives in the handler. A visit that was never confirmed is still refused by the domain as before.
- **The contact notification has the route key `admin-contact`.** The app has no screen for it yet, so tapping it opens the notification list only (same as `admin-investment`).
- **Listings unpublished after being saved vanish from the favorites list and return when published again** (the row is kept).

## Deliberately not solved

| ID | Why |
|---|---|
| D07 | Parallel confirm writes five notifications; the final state is correct. Needs an `xmin` token on `VisitRequest`, which changes the model snapshot. Repeated cancel answers 204. |
| Lead de-duplication | Product decision: a repeated submit may be a deliberate second contact. |
| Orphan favorite of a deleted listing | The row is invisible (the query filter follows the listing) and harmless; removing it needs a cleanup job, not worth it. |
| D08 / D09 | Fixed by the launch-audit PR (#372), already merged. |
