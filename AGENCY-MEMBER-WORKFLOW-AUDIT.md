# AGENCY-MEMBER-WORKFLOW-AUDIT

Audit date: 2026-10-08 · Code audited: `origin/master` @ `ec17ea9` (API repo). Frontend repo (`HudhudNest`, branch `chore/remove-legacy-names`) was **read only**.

**Evidence level.** Every finding below is proven from the source code. Behaviour changes are proven by new unit tests (Application.Tests 1393 / Auth.Tests 333 / Architecture.Tests 183 / Infrastructure.Tests 216 pass, `dotnet format --verify-no-changes` clean). **Not done:** a live end-to-end run on Staging and database-level checks. Reason: the Staging test secrets were deleted after the previous QA run, Docker (needed for the Postgres integration tests) is not running locally, and Staging does not contain this change until it is deployed. Those items are listed under *Test Results* as NOT RUN, not as passed.

## Executive Summary

The core messaging path already works and is secure: the recipient is derived by the backend from the listing's publisher, a customer cannot redirect the message, replies stay on the same listing thread, and nothing mixes conversations of different listings. Two required capabilities were **missing**: (1) the agency owner was never notified of an inquiry to a member (fixed in this change), (2) there is no Agency rating at all, and the listing detail does not carry agency information a client can display. Several business rules (member removal, agency/member deactivation, owner visibility of conversations, manager role) are undefined in code and need your decision.

## Current Architecture

| Concept | Where it lives |
|---|---|
| Agency | `Agency` (Name, Slug, Logo, Contact, `OwnerUserId`, `IsActive`, soft delete) |
| Agency member | **No AgencyMember entity.** Membership = `UserAccount.AgencyId` (+ `AgencyJoinedAt`), one agency per user, plus identity role `AgencyAgent`; owner has role `AgencyOwner` |
| Agency manager | **Does not exist** (only Owner and Agent) |
| Listing | `Property`: `OwnerId` (= the publishing user) and `AgencyId` (nullable snapshot) |
| Message | `Message`: SenderId, ReceiverId, PropertyId. **No Conversation entity**; a conversation is the set of messages of (PropertyId, userA, userB) |
| Notification | `Notification`: RecipientId, Type, Message, PropertyId, RelatedEntityId |
| Ratings | `UserRating` (rates a user, 4 required + 2 optional criteria) and `PropertyReview` (rates a listing, requires a completed visit). **No agency rating** |

## Current Behavior vs Required Behavior, Gap Analysis

| # | Requirement | Finding (proof) | Status |
|---|---|---|---|
| 1 | Listing owner = publisher | `Property.OwnerId` is set from the authenticated user (`CreatePropertyCommandHandler`) | OK |
| 2 | Agency attribution derived server-side | `property.SetAgency(ownerAccount.AgencyId)` at creation; `CreatePropertyCommand` carries no AgencyId, so a client cannot attribute a listing to another agency | OK |
| 3 | Individual vs agency listing | `AgencyId == null` for non-members; no agency artefacts are created | OK |
| 4 | Agency A member cannot publish for Agency B | Agency comes only from the caller's own account | OK |
| 5 | Membership/agency active check at publish | Was missing; now enforced on create and publish (rules 2-3) | OK (new) |
| 6 | Agency + publisher shown on listing detail | Was: `PropertyDto` carried only `AgencyId`. Now the public list and detail add `AgencyName`, `AgencySlug`, `AgencyLogoUrl`, `AgencyRatingAverage`, `AgencyRatingCount` for an **active, non-deleted** agency (one batch lookup per page); null for individual listings and inactive agencies. The frontend block that shows it is not built | OK (API) |
| 7 | Message recipient from backend | `SendMessageCommandHandler`: for a non-owner sender, `receiverId = property.OwnerId`; any `ReceiverId` in the request is ignored | OK |
| 8 | Owner reply restricted | The owner must name a `ReceiverId`, and `ConversationExistsAsync(property, owner, receiver)` must be true, otherwise 403 | OK |
| 9 | Agency owner in-app notification | **Absent** before this change (only the receiver was notified) | **GAP, fixed** |
| 10 | Notification content/privacy | New notification names customer, member, listing and agency; it does not copy the message text | OK (new) |
| 11 | Conversation per listing | Messages carry `PropertyId`; thread queries filter by property and participants, so two listings never mix; no Conversation row exists, so duplicate conversations cannot occur | OK |
| 12 | Two-way conversation | Customer -> publisher, publisher -> customer, customer again, all in the same (property, pair) thread | OK (by code) |
| 13 | Agency owner != publisher | Message goes to the publisher; the owner only gets the awareness notice (new) | OK |
| 14 | Dual rating, member | `UserRating` on the member's user id: self-rating blocked, one rating per rater/rated (upsert, not unlimited), eligibility = completed visit **or** any message thread with that user | OK |
| 15 | Dual rating, agency | New `AgencyRating` aggregate (table `AgencyRatings`, criteria Service / Professionalism / Reliability / OverallExperience, 1..5, optional comment, unique per agency+rater) with its own endpoints; it never reads or writes `UserRating`. Rules: agency active; owner and members cannot rate; a completed visit or a message thread about a listing currently attributed to the agency is required; repeat call edits in place | OK (API) |
| 16 | Agency manager | Decided: the manager is the owner; no new role (rule 6) | OK (decision) |
| 17 | Member removal | Was: listings kept the agency attribution. Now: they become individual (rule 1) | OK (new) |
| 18 | Member deactivation | Was: 404 to the customer. Now: routed to the agency owner while the agency is active (rule 4, interpretation) | OK (new) |
| 19 | Agency deactivation | Now blocks new listings and publishing (rules 2-3); existing listings and the public profile behaviour are unchanged | OK (new) |
| 20 | Owner sees member conversations | Implemented read-only (rule 5) | OK (new) |

## Agency Ownership / Listing Publisher / Display Flow

Agency -> member (`AgencyId` on account) -> listing (`OwnerId` = member, `AgencyId` = agency at creation). The chain is verifiable in data (`Properties.OwnerId`, `Properties.AgencyId`, `AspNetUsers`/`UserAccounts.AgencyId`). Display is the gap (row 6): the data exists, the public listing contract does not carry agency name/logo/rating and there is no agency-by-id public lookup.

## Messaging, Notification, Reply Flow

Customer `POST /api/messages` -> backend resolves the publisher -> message stored -> publisher notified (existing `NewMessage`) -> **new:** if the listing has an agency whose owner is neither sender nor receiver, the owner gets `AgencyMemberInquiry` ("<customer> sent a message to <member> about '<listing>' (agency '<agency>')"), linked to the listing and message id; failure of this notification never fails the message (same pattern as the existing one). Publisher replies through the same endpoint with `ReceiverId` = customer (allowed only if a thread exists); customer replies by calling again.

## Dual Rating Flow

Member rating works (`POST /api/users/{id}/ratings`, eligibility endpoint). Agency rating does not exist (row 15). Recommended minimal implementation, **not built**: an `AgencyRating` entity (AgencyId, RaterId, score, optional comment, unique per rater+agency), eligibility = the customer messaged or visited a listing of that agency, owner/members of the agency cannot rate it, and its own endpoint; it must not feed `UserRating`. This needs a migration and a product decision on criteria, so it is left for your approval.

## Authorization / IDOR / Privacy Analysis

- Recipient manipulation: blocked (test `Customer_SuppliedReceiverId_IsIgnored`).
- Replying into a thread that does not exist: blocked (test `NonPublisherReply_WithoutConversation_Throws`).
- Reading someone else's conversation: message queries filter by participants (`MessageRepository`); the earlier Staging run (M3) confirmed a third user sees nothing.
- Editing another member's listing: ownership check is by `OwnerId` (Staging run Z1-Z7 passed); an agency owner has **no** extra right over members' listings in code.
- Privacy note: the public agency profile (`AgencyDto`) returns `OwnerUserId`, contact e-mail/phone, licence number and the full member list. Confirm this is intended for an anonymous endpoint.
- Not tested live: agency-owner access to agency data by role, agency B user acting on agency A.

## Database / API / Frontend / Localization

- Database: no schema change was needed (`Notifications.Type` is an int enum; value 39 added). Row-level checks were NOT RUN.
- API: unchanged contracts. `INotificationService` gained one method; `SendMessageCommandHandler` gained an `IAgencyRepository` dependency.
- Frontend: read only; not changed. The `Notification` list shows the stored `Message` text, so the new notice renders without client change, but any client switch on `NotificationType` needs the new value 39 (not verified). Agency block on the listing detail and any agency-rating UI do not exist.
- Localization: `NewMessage` and `AgencyMemberInquiry` are localized at read time (ar/en/de, unit-tested); every other notification type, the live push and API error messages remain Arabic. EN/DE/RTL rendering in the UI was not tested.

## Test Results

| Scenario | Result |
|---|---|
| Customer -> agency member: message to publisher + owner notified, no message text in notice | PASS (unit) |
| Individual listing: no agency notification | PASS (unit) |
| Owner is the publisher: no duplicate notification | PASS (unit) |
| Publisher reply: no agency notification | PASS (unit) |
| Wrong recipient (customer-supplied ReceiverId) | PASS (unit) |
| Non-publisher reply without thread | PASS (unit) |
| Notification failure does not fail message | PASS (unit) |
| Existing suites (Application 1393, Auth 333, Architecture 183, Infrastructure 216) and formatting | PASS |
| Live E2E on Staging (steps 1-17), race conditions, two listings same customer, DB tracing, EN/DE/RTL, agency/member deactivation behaviour | NOT RUN (see Evidence level) |
| Agency rating, agency info display, manager role | MISSING in the system |

## Bugs / Security Findings

- No exploitable authorization defect found in the messaging path.
- STG-class defects from the previous QA run are tracked separately (PR #297).

## Business Rules (decided by the product owner, 2026-10-08, and implemented)

| # | Rule | Implementation |
|---|---|---|
| 1 | When a member leaves, their current listings become individual | `RemoveAgencyMemberCommandHandler` calls the new `IAgencyRepository.ClearMemberAttributionAsync` (sets `Property.AgencyId = null` for that member's listings of that agency; they stay published and owned by the user) in the same save as the membership removal |
| 2 | A deactivated agency blocks new listings | `CreatePropertyCommandHandler`: a member of a deactivated/deleted agency gets 403 `AGENCY_INACTIVE`; existing listings are untouched |
| 3 | Publishing requires an active agency | `PublishPropertyCommandHandler`: publishing an agency listing as non-admin requires an active agency (403 `AGENCY_INACTIVE`); administrators are exempt. Creating a listing already publishes it, so rule 2 covers creation |
| 4 | Disabled member: inquiries go to the agency owner | **Interpretation:** the answer was phrased as a question; it was implemented as "yes" and is isolated in `SendMessageCommandHandler` (revert = remove the `representativeId` block). If the publisher is not an active account and the listing's agency is active, the customer's message goes to the agency owner, who can reply in the same thread. No routing if the agency is inactive (the existing 404 applies) |
| 5 | The agency owner may read members' conversations | Read-only. `GetMessagesQueryHandler`: for a listing of their agency the owner can list all messages and read the publisher<->customer thread (`OtherUserId` = customer). `GetConversationsQueryHandler`: with `propertyId` of an agency listing the owner sees the publisher's conversations for it (unread counts are the publisher's). Marking as read and sending stay participant-only |
| 6 | Agency manager = agency owner | No new role. The owner is the manager everywhere (notifications, oversight, member removal) |

Note: customers are not told that the agency owner can read the thread; consider stating it in the privacy text.

## Added after the decisions (2026-10-08, third change set)

- **Agency rating:** `GET /api/agencies/{id}/ratings` (public, rate-limited `public-read`, 404 for an inactive agency), `GET /api/agencies/{id}/ratings/eligibility` and `POST /api/agencies/{id}/ratings` (authenticated; rater = token). Migration `AddAgencyRatings` (new table only). The account data export gained `AgencyRatingsGiven`.
- **Agency card on the listing contract:** see row 6. Cached list/detail responses can show a rating up to the existing output-cache TTL (30 s) late.
- **Notifications in the site language:** `GET /api/notifications` accepts `?language=ar|en|de` (regional forms accepted; `Accept-Language` is the fallback; same contract as the confirmation mail). The text of `NewMessage` and `AgencyMemberInquiry` is rendered in that language when the user reads their list, from the live names (customer, member, listing, agency) with localized fallbacks. Without a language, or with an unsupported one, the stored Arabic text is returned unchanged.

## Still open

- **Other notification types stay Arabic-only** (visit, review, listing lifecycle, invitations, services, short stay, valuation, ...): only the two types of this workflow are localized. The real-time push (SignalR) carries the stored Arabic text because the recipient's site language is unknown when the event is raised; the list endpoint is localized. The frontend must send `language` (it does not store or send a preferred language today) and show the new agency fields / rating UI: **not built**.
- The live end-to-end run on Staging and the DB-level checks remain NOT RUN.
- Error messages (403/400) are still Arabic only.

## Recommended Fixes

1. Deploy these changes and run the live E2E (needs the Staging secrets again).
2. Add `AgencyName`, `AgencyLogoUrl`, `AgencySlug` to the listing detail contract (or a public agency-by-id endpoint) and an agency block in the frontend.
3. Decide and build the agency rating (section Dual Rating Flow).
4. Resolve the business rules above.

## Final Verdict

```text
PASS WITH WARNINGS
```

Backend (API + domain + unit tests): every step of the acceptance chain is now implemented: member publishes, agency and publisher shown on the listing contract, message to the publisher, agency-owner notification in the reader's language, reply and continued thread, member rating and a separate agency rating. Warnings: the live Staging end-to-end run and DB-level checks were NOT RUN; the frontend does not yet send the site language, show the agency card, or offer the agency rating; other notification types are still Arabic-only.
