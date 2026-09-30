# Media Storage Architecture (Cloudinary)

Status: current as of the per-entity folder redesign (2026-09-18). Supersedes the flat-folder
layout described informally in [ARD-HudhudNestApi.md](ARD/ARD-HudhudNestApi.md) ADR-004.

## 1. Folder structure

```
realestateworld/
├── agencies/{agencyId}/logo/
├── users/{userId}/profile/
├── properties/{propertyId}/images/
├── short-stay/{listingId}/photos/        (reserved — see §11, no upload path exists yet)
├── investments/{investmentId}/documents/
├── services/{serviceRequestId}/documents/
└── system/social/{propertyId}/share/     (system-generated share/OG images)
```

- The root segment (`agencies`, `users`, `properties`, `short-stay`, `investments`, `services`,
  `system/social`) is fixed per `MediaEntityType`.
- The second segment is always the **owning entity's own database id** (a GUID) — never a name,
  slug, or anything a user could change. An agency's logo folder is `agencies/{agencyId}/logo`,
  never `agencies/AlYaqeenOffice/logo`.
- The last segment is a short category slug (`logo`, `profile`, `images`, `photos`, `documents`,
  `share`) distinguishing kinds of media within one entity's space.
- `PropertyImageType` (General/Exterior/Interior/Plan/Document) and `IsMain`/`SortOrder` remain
  **database columns on `PropertyImage`**, not folder segments — the primary/cover image and its
  category are always read from the database, never inferred from Cloudinary folder order or
  position (see §5).

## 2. Naming convention

Every folder is built by exactly one class: `MediaFolderBuilder`
(`HudhudNestApi.Application/Common/Services/MediaFolderBuilder.cs`), which implements
`IMediaFolderBuilder`:

```csharp
string BuildFolder(MediaEntityType entityType, Guid entityId, string mediaCategory);
```

- `entityType` — one of `Agency`, `User`, `Property`, `ShortStayListing`, `Investment`,
  `ServiceRequest`, `Social` (`HudhudNestApi.Application.Common.Enums.MediaEntityType`).
- `entityId` — must not be `Guid.Empty`.
- `mediaCategory` — must match `^[a-z][a-z0-9-]*$` (single lowercase slug, no slashes, no path
  traversal). The standard values live in `MediaCategories`
  (`HudhudNestApi.Application/Common/Models/MediaCategories.cs`): `Logo`, `Profile`, `Images`,
  `Photos`, `Documents`, `Share`.

This is registered as a singleton in `HudhudNestApi.Application/DependencyInjection.cs` — it is
pure/deterministic (no I/O), so one instance is safe to share across all requests, the same
reasoning already used for `PwnedPasswordsCircuitBreaker` and
`TemplateSocialContentGenerator` in that file.

## 3. Entity mapping

| Media | `MediaEntityType` | Entity id passed in | Category |
|---|---|---|---|
| Property images | `Property` | `PropertyImage.PropertyId` (i.e. the property being uploaded to) | `images` |
| Agency logo | `Agency` | `Agency.Id` | `logo` |
| User avatar | `User` | `UserAccount.Id` | `profile` |
| Investment project document | `Investment` | `InvestmentProject.Id` | `documents` |
| Service request document | `ServiceRequest` | `ServiceRequest.Id` | `documents` |
| Social share image | `Social` | the source `Property.Id` (nested under `system/social`, not under `properties/`, since it's a generated derivative, not part of the property's own gallery) | `share` |

## 4. Upload flow

1. Controller receives the multipart request, maps it to a MediatR command carrying the raw file
   bytes/stream plus the **target entity id taken from the route**, never from the request body.
2. The command handler authorizes the caller against that entity first (ownership/role checks —
   see §8). No folder is computed before this check passes.
3. The handler validates the file (size, MIME type, extension, magic-byte signature — see §7).
4. The handler calls `IMediaFolderBuilder.BuildFolder(entityType, entityId, category)` to get the
   folder, then `IMediaStorageService.UploadImageAsync(content, fileName, contentType, folder,
   ct)`.
5. On success, the handler persists the returned `Url`/`PublicId` on the owning entity's own
   columns (see §9 — there is no separate `Media` table) and calls `SaveChangesAsync`.
6. If a previous asset is being replaced (avatar, agency logo), the old `PublicId` is deleted
   from storage **only after** the new state is safely persisted, in a try/catch that never turns
   a successful upload into an error response (non-critical cleanup).

The client (Angular) never sends a folder, path, or `PublicId` — it only ever uploads a plain
`FormData` file and reads back a plain URL string. There was no folder/path/publicId client
input to remove; none existed before this change either (confirmed by a full-repo audit).

## 5. Delete / replace flow

Unchanged by this redesign — the existing pattern was already correct and is preserved:

- **DB write is authoritative; Cloudinary cleanup is best-effort afterward**, wrapped in
  try/catch so a storage failure never blocks or rolls back a successful DB state.
- Deleting an entity (property, short-stay listing, user account) captures the `PublicId`s
  before the row disappears behind a soft-delete filter, then loop-deletes each Cloudinary asset
  in a per-item try/catch.
- Multi-file upload (property images) tracks every `PublicId` uploaded so far in the batch and
  cleans all of them up if a later file in the same batch fails (a real compensating-transaction,
  not just best-effort).
- **Fixed as part of this change**: `RemoveInvestmentDocumentCommandHandler` used to swallow a
  failed Cloudinary delete with a bare `catch { }`, leaving no way to ever find the orphaned
  asset. It now logs a warning (`ILogger<RemoveInvestmentDocumentCommandHandler>`) with the
  document id and storage key, without changing the non-throwing behavior — the DB row is still
  the source of truth and its removal is never rolled back by a storage failure.

## 6. Authorization

Folder computation happens **after** authorization, using only the already-authorized entity's
own id — the folder itself carries no independent trust decision. Existing ownership checks
(unchanged by this work):

| Upload | Check |
|---|---|
| Property images | `IPropertyOwnershipService.EnsureOwnerAsync` — `property.OwnerId == callerId` |
| Agency logo | `[Authorize(Roles = AgencyOwner)]` **and** `agency.OwnerUserId == callerId` (the role alone only proves ownership of *some* agency, not this one — Agency has no row-level tenant isolation, see [ARD-HudhudNestApi.md](ARD/ARD-HudhudNestApi.md)) |
| User avatar | Always the caller's own `UserId` from the JWT — there is no "target user" parameter to spoof |
| Investment documents | `[Authorize(Roles = Admin)]` — platform-managed, not user-owned |
| Service request documents | `serviceRequest.RequesterId == callerId \|\| provider.UserId == callerId` |

Because the entity id used to build the folder is always the same id the ownership check just
validated (never a second, unchecked id from the request), there is no path by which a caller can
choose a different entity's folder — computing `agencies/{agencyId}/logo` for an agency the
caller does not own is impossible without first passing `agency.OwnerUserId == callerId`.

## 7. Upload validation (unchanged, documented here for completeness)

Size/MIME/extension/magic-byte-signature checks are per-handler (property images 5MB
jpeg/png/webp, avatar/logo 2MB jpeg/png, investment documents 15MB pdf/jpeg/png/webp, service
request documents 5MB jpeg/png/webp). **Fixed as part of this change**:
`UploadServiceRequestDocumentCommandHandler` claimed in its own doc comment to have the same
magic-byte signature check as property images, but the check was missing — it now performs the
same JPEG/PNG/WEBP header validation as `UploadPropertyImagesCommandHandler`.

`AddInvestmentDocumentCommandHandler` still allows PDFs without a magic-byte check (PDF has no
short fixed magic-byte signature as reliable as image formats) — this is a pre-existing,
documented gap, not introduced or fixed by this change (see §12).

## 8. Database metadata

No new `Media` table was introduced. Each entity continues to own its `Url`/`PublicId` columns
directly (`PropertyImage`, `Agency.LogoUrl/LogoPublicId`,
`UserAccount.ProfileImageUrl/ProfileImagePublicId`, `InvestmentDocument.Url/StorageKey`,
`ServiceRequestDocument.FileUrl/FilePublicId`, `SocialMediaAsset.FileUrl/StorageKey`) — this was
a deliberate choice: every metadata field the redesign needs (entity id, the media's own
`PublicId`, and for `PropertyImage` specifically `IsMain`/`SortOrder`/`ImageType`) already
existed. The Cloudinary **folder** is not stored anywhere — it's recomputed on demand from
`(EntityType, EntityId, Category)`, which are already known wherever a `PublicId` is known, so
storing the folder string too would be pure duplication.

## 9. Legacy assets / migration

**No migration was performed and none is planned as part of this change.** Every asset uploaded
before this change lives in the old flat folders (`property-images`, `user-avatars`,
`agency-logos`, `investment-documents`, `service-request-documents`, `social-assets`) and stays
there — their `PublicId`s and `SecureUrl`s are unaffected and keep working exactly as before,
since Cloudinary folder naming has no bearing on an already-issued URL's validity. Only **new**
uploads (from this change forward) land in the new per-entity structure. This was an explicit,
deliberate decision: bulk-renaming live production media/DB rows was judged higher-risk than the
benefit of a fully-migrated Cloudinary console, matching the stated priority order
(**Data Safety > Authorization > Backward Compatibility > ...**). If a fully unified layout is
wanted later, it needs its own separate migration task with a dry-run/rollback plan — see §12.

## 10. How to add a new media type

1. Add a case to `MediaEntityType` (`HudhudNestApi.Application/Common/Enums/MediaEntityType.cs`)
   and to the `switch` in `MediaFolderBuilder.BuildFolder`.
2. Add any new category slug needed to `MediaCategories`.
3. In the new feature's upload command handler: authorize the caller against the target entity
   first, then call `_folderBuilder.BuildFolder(newEntityType, entity.Id, category)` and pass the
   result as `folder` to `IMediaStorageService.UploadImageAsync`.
4. Add a `MediaFolderBuilderTests` case asserting the new entity segment, and a handler test
   asserting the folder is derived from the entity id (see
   `tests/HudhudNestApi.Application.Tests/Common/MediaFolderBuilderTests.cs` and
   `UploadUserAvatarCommandHandlerTests` for the pattern).

## 11. Known pre-existing gaps (not introduced or fixed by this change)

- **`ShortStayListingPhoto`** has a DB table and a Cloudinary folder reserved above, but no
  command/endpoint currently creates a row for it — an unfinished feature from before this
  change. Uses the same `MediaEntityType.ShortStayListing` → `short-stay/{listingId}/photos`
  path once built.
- **`ServiceProvider.LogoUrl`/`LogoPublicId`** and its `SetLogo()` domain method exist but have
  zero production callers.
- **Cloudinary Staging/Production isolation is credentials-only, not storage** (documented
  pre-existing risk R4 in [ARD-HudhudNestApi.md](ARD/ARD-HudhudNestApi.md)): both environments upload
  to the same folder namespace, distinguished only by which `Cloudinary:*` API key is configured.
  This redesign does not change that — it was out of scope (not the folder-per-entity problem
  this task addressed). A future mitigation would prefix the root segment with the environment
  name (e.g. `realestateworld-staging/...`).
- **`AddInvestmentDocumentCommandHandler`** allows PDF uploads without any magic-byte signature
  check (pre-existing; PDFs don't have as short/reliable a fixed header as image formats do).

## 12. Frontend

No Angular changes were needed or made. The Angular app (`HudhudNest`, a separate repository)
never sent or received a folder/path/`PublicId` for any upload before this change, and still
doesn't — every upload flow (avatar, property images, short-stay photos, investment/service
documents) goes through one shared `BackendApiService.upload()` using plain `FormData`, and only
ever reads back a plain URL string. The folder redesign is entirely a backend-internal concern.
