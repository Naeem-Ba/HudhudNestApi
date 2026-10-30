# Services marketplace and valuation: QA Phase 11 fixes (2026-10-30)

Audit report: `qa-plan/phase-11-services-valuation.md` (project folder). This file records what was changed, the
choices made without a ruling from the owner, and what is deliberately **not** solved.

Nothing here deploys itself. **No migration, no schema change.** Rollout: merge, Render deploy.

## What changed

| ID | Problem found | Fix | Where |
|---|---|---|---|
| D01 a, c (P2) | The valuation contact consent stored any text as the customer's phone/email and delivered it to the office; a 500-character email was a 500 | Phone must match the shape the provider profile uses, email must be an address of at most 320 characters (422) | `SubmitValuationContactConsentCommandValidator` |
| D02 (P2) | The anonymous valuation request answered 500 for a governorate, district, neighborhood or property type that does not exist, and for an area at the decimal maximum; area and rooms had no upper bound | Locations must exist and agree with each other (`ILocationHierarchyChecker`, which also rejects a district of another governorate), the property type must exist, area uses `WithinAreaRange`, rooms 1 to 50 (the same as a listing) | `CreateValuationInquiryCommandValidator` |
| D03 (P2) | An unknown currency id was a 500 on offering create/update, accept and complete | `MustBeActiveCurrency` (422) on all four | `ValidationRuleExtensions`, validators |
| D04 (P2) | A provider email of 500 characters and an accept note of 100000 characters were 500s | Email at most 320, note at most 1000 (the column sizes); the accept command gets the validator it never had (price not negative and under 1e12, no NUL) | `ServiceValidationLimits`, `AcceptServiceRequestCommandValidator` |
| D05 (P3) | Admin create provider for an unknown user was a 500; verification level 99 was stored; a duration of 2e9 days and a schedule in year 9999 were accepted | 404 for the user; `IsInEnum` for the level; duration 0 to 3650 days; schedule at most 5 years ahead. The schedule rule read `DateTime.UtcNow` once when the validator was built; it is now read per call | handlers, validators |
| D06 (P3) | Two reviews racing for one request: one stored, the other a 500 | `IX_ServiceReviews_ServiceRequestId` maps to 409 | `UnitOfWork.UniqueViolationConflictMessages` |
| D08 (P3) | An offering switched off, or one of a suspended provider, stayed readable by id; a suspended provider could still add or re-enable offerings | The public read answers 404 under the catalog's own rule (active offering, Active provider); a suspended provider gets 409 when creating or re-enabling an offering (switching one off is still allowed) | `GetServiceOfferingByIdQueryHandler`, offering handlers |
| D09 (P3) | A service request could be opened on somebody else's unpublished listing, and the provider then saw its title | 404 unless the listing is published or the requester owns it | `CreateServiceRequestCommandHandler` |
| D10 (P3) | Documents were accepted on requests that ended without service | 409 for Rejected, Cancelled, NotCompleted and Expired. A completed request still takes documents (the provider's report arrives after completion) | `UploadServiceRequestDocumentCommandHandler` |
| D11 (P3) | The office flag reason of 100000 characters was a 500; clearing a flag that was not set wrote an audit row | Reason at most 2000 (400); clearing an unset flag answers 200 and writes nothing. The huge page number is fixed in the launch-audit PR (#372), not here | `AdminValuationInquiryService` |

## Choices made without a ruling from the owner

- **Currency must be active**, not merely present: an inactive currency should not price new work.
- **Rooms 1 to 50**, the same bound a listing has.
- **Schedule limit 5 years, duration 10 years**: far beyond any real visit, well inside the types. Not business rules.
- **Documents stay open after Completed.**

## Deliberately not solved

| ID | Why |
|---|---|
| D01 b | The consent is documented as create-once ("the customer consented, on this date, to share this contact"). Letting the customer correct a wrong number changes that contract, so it is a product decision. Today a wrong number cannot be fixed. |
| D01 d | On a guest inquiry the inquiry id is the only secret, and the status endpoint lists the invitation ids. Anyone who holds the id could write the contact first. The id is a random GUID that only the creator receives (a capability link); a separate token would be a contract change on the app too. Not exploitable without the id. |
| D07 | Two parallel `complete` calls write two history rows and two notifications; the final state is correct. The fix is an `xmin` concurrency token on `ServiceRequest`, which changes the model snapshot that the launch-audit PR (#372) also changes. Do it after #372 merges, if a double tap proves common. |
| Suspended provider editing the profile | Harmless; the profile is not public while suspended. |
| Page overflow, DB values mapped to 422/409 in general | Already in the launch-audit PR (#372). |
