# Automatic listing translation

Translates a listing's **title** and **description** (not structured data, not user messages) between
Arabic, German and English, stores the result permanently in PostgreSQL and reuses it for every user.
Covers sale/rent listings and short-stay listings.

## Endpoints

| Listing kind | Endpoint |
|---|---|
| Sale / rent | `POST /api/properties/{id}/translation?lang=ar\|de\|en` |
| Short-stay | `POST /api/short-stay/listings/{id}/translation?lang=ar\|de\|en` |

Anonymous, rate limit `listing-translation` (30/min per client), `Cache-Control: no-store`. POST on
purpose: a first call can spend provider quota and must not be triggered by prefetchers. The listing
detail endpoints are unchanged and never wait for a translation.

Answer: `status` is `ready`, `pending` (another request is translating; ask again after
`retryAfterSeconds`), `same_language` or `unavailable`; each field (`title`, `description`) has its own
state. 404 when the listing is missing, deleted, unpublished, expired or taken down.

## Storage and validity

Table `ListingTranslations`, one row per (listing, field, target language), unique per listing kind
(partial unique indexes; exactly one of `PropertyId`/`ShortStayListingId` is set, enforced by a check
constraint). Rows cascade-delete with a permanently deleted listing; a soft-deleted listing keeps its rows
but they are unreachable (EF filter, endpoint 404).

A stored row is valid only if its `SourceHash` equals the SHA-256 (hex, UTF-8) of the listing's **current**
text and its `PolicyVersion` equals the configured one. This is checked on every read, so an edit
invalidates exactly the edited field, with no hook in the edit path. Re-translation happens at the first
later request.

## Concurrency

A request claims the missing/stale fields in one transaction under `pg_advisory_xact_lock` and an
`INSERT ... ON CONFLICT ... DO UPDATE ... WHERE <stale | abandoned lease | cooldown passed>`; fields of
one request go to the provider in one batched call per source language. The result is committed only if
the claim token still matches and the listing is not deleted and its text hash is unchanged. Works across
instances; a crashed instance's claim expires after `ClaimLeaseSeconds`.

## Failure, cost and switches

- Provider call: timeout `ProviderTimeoutSeconds` (6), independent of the client request.
- Failures: retry after 30 s x attempts, then `LongCooldownMinutes` (6 h) after `MaxAttempts` (3); permanent
  provider errors use the long cooldown at once. Quota exhaustion: `QuotaCooldownMinutes` (10).
- Monthly budget: `MonthlyCharacterLimit` (400,000; DeepL API Free allows 500,000) reserved atomically
  before each provider call (`TranslationMonthlyUsages`). Stored translations are still served at zero.
- Switches: `Enabled` (everything off), `CreationEnabled` (serve stored, create nothing), `PolicyVersion`
  (bump to invalidate everything). A store/database error answers `unavailable`, never a 500.
- Text above `MaxFieldCharacters` (6000) or of unclassifiable language is left alone.

## Provider

`ITranslationProvider` (batch). Default `None`: nothing is sent anywhere. To enable DeepL set, on the server
only: `ListingTranslation__Provider=DeepL` and `ListingTranslation__DeepL__ApiKey=<key>` (a key ending in
`:fx` uses api-free.deepl.com). With DeepL selected and no key the API logs a warning at first use and
translates nothing. The app never holds a key.

## Rollout (manual)

1. Run the Migrator (`AddListingTranslations`, then `ListingTranslationsShortStay`; both additive).
2. Deploy the API.
3. Set the provider variables; test on Staging with a real key (not yet done by anyone).
4. Release the app (main -> release, Netlify publish; new Android build for the native app).

## Not verified

No real provider call has ever been made (no key). DeepL's Arabic support and Free-plan terms for this use
are to be confirmed by the owner. Translation quality, and the real-device/RTL look, are not measured.
