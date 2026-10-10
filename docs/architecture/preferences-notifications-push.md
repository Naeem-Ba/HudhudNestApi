# Language, currency, notification language and push

> Built 2026-10-10 on `feat/user-preferences-localized-notifications-push` (API and web/app repos).
> Related: `docs/architecture/email-confirmation-and-delivery.md`.

## What this is

Each account has two independent preferences -- the **interface language** (`ar`, `en`, `de`) and the
**display currency** (`EUR`, `USD`, `SYP`, `TRY`, `AED`, `SAR`). Notifications follow the **recipient's
interface language**; there is no separate "notification language" setting.

Neither is derived from country, IP or device language once the user has chosen. Those signals only
pick the *first* language of a visitor who has never chosen (`first-visit-language.ts`, unchanged).

## Data model

| Where | Change |
|---|---|
| `UserAccounts.PreferredLanguage` / `PreferredCurrency` | existing columns, reused |
| `UserAccounts.PreferredLanguageSetAt` / `PreferredCurrencySetAt` | **new**, nullable `timestamp` |
| `DeviceTokens` | **new** table: `Id`, `UserId` (FK, cascade), `Token` (unique, 512), `Platform`, `CreatedAt`, `LastSeenAt` |

Migration `20261010194919_AddPreferenceMarkersAndDeviceTokens` is additive only (two nullable columns,
one table). No data is rewritten and there is nothing destructive in `Down` beyond dropping what `Up`
created.

**Why the `*SetAt` columns exist.** Every account already in production carries the column defaults
(`en` / `EUR`), because no client ever wrote these fields (`PUT /users/me` hard-codes `null` for them).
Treating that `en` as a choice would flip every existing Arabic-speaking user's notifications to English
on deploy. `SetAt = null` means "never chose": notifications keep the legacy Arabic wording, the API
reports `hasChosenLanguage=false`, and the app uploads the language the device is already showing the
first time that account signs in.

## API

| Endpoint | Purpose |
|---|---|
| `GET /api/users/me` | now also returns `preferredLanguage`, `preferredCurrency`, `hasChosenLanguage`, `hasChosenCurrency` |
| `PUT /api/users/me/preferences` | **new**. Body `{ preferredLanguage?, preferredCurrency? }` (null = unchanged). Reads only those two fields; the user id comes from the token. Unsupported code = `400`. Rate limit `user-write`. |
| `POST /api/devices/push-token` | **new**. `{ token, platform: android\|ios\|web }`. Registers or re-binds the token to the caller. |
| `POST /api/devices/push-token/revoke` | **new**. `{ token }`. Removes it only if it belongs to the caller; idempotent. |
| `POST /api/auth/register` | `language` is now stored as the account's language when supported |

The allowed codes live in one place: `Domain/Users/Constants/SupportedPreferences.cs`. The validator, the
entity and the notification renderer all read it. The web app mirrors it in
`core/i18n/supported-preferences.ts`.

## Notification language

* Callers (handlers) describe *what happened* with `NotificationText.Of(key, args)`; they never write a
  sentence. `NotificationService` looks up the **recipient's** language (`IRecipientLanguageResolver`,
  reading only the recipient's own `UserAccounts` row) and renders from
  `Infrastructure/Notifications/Localization/NotificationCatalog.cs` (ar/en/de for every key).
* The language of whoever triggered the event, and anything the HTTP client sends, never takes part.
* User-written text (a property title, a visit note) is inserted verbatim, never translated, never
  re-scanned for placeholders.
* A blank translation falls back to English then Arabic; an unknown key renders empty rather than a raw
  key. Tests assert all three languages are filled in and use identical placeholders.
* Already stored notifications are never rewritten, so a language change affects only later ones. The
  `?language=` query the web app still sends to `GET /notifications` is ignored by the server.
* Not yet localized: **security-alert emails** (`SecurityAlertEmailService`, English only, addressed by
  email rather than account id, with placeholder links) -- see "Remaining work".

## Push

* `NotificationService` persists, sends over SignalR as before, then calls `IPushNotificationDispatcher`
  with the *same stored, localized text*. A push failure or a 5 s timeout is logged (no message content)
  and never fails the business operation.
* `FcmPushSender` speaks FCM HTTP v1 and authenticates with a service account (RS256 JWT -> OAuth token,
  cached). One sender serves Android directly and iOS through APNs. **No new NuGet package.**
* Configuration (`Push:Fcm`): `Enabled`, `ProjectId`, `ServiceAccountJson` (a secret: set
  `Push__Fcm__ServiceAccountJson` in the host's secret store, never a file in the repo). Without it the
  no-op sender is used and only in-app notifications are delivered. Nothing changes for Production until
  these are set.
* A token that FCM reports as unregistered is deleted. Registering a token that already exists moves it
  to the new account, so a notification for the previous account on a shared phone cannot ring there.
  At most 20 tokens per account are kept.

## Clients

Web/app: `PreferencesService` is the single owner.

* Signed in: the server is the truth. A change is **saved first**, then applied; a failed save keeps the
  old value and shows an error. Account values are shown with `LanguageService` persistence `account`,
  which never writes the device-wide `lang` key, so they cannot leak to the guest or to the next account.
  A per-user cache (`prefs:<id>`, language and currency only) prevents a wrong-language flash on reload.
* Never chosen on the account: the device's existing choice is uploaded once instead of adopting the
  server default.
* Sign-out: the device returns to its own guest language/currency.
* Language and currency setters never touch each other.
* Money is formatted with the language on screen (`uiNumberLocale()`), not a hard-coded `ar`/`en`.

### Display currency today

Prices are **shown in the currency each listing was published in**. The chosen display currency is stored
and synced, but no exchange-rate source exists, so nothing is converted and a currency symbol is never put
on an unconverted number. `core/pricing/price-display.ts` (`ExchangeRateProvider`, `describePrice`) is the
ready seam: it converts only with a valid rate for exactly that currency pair and otherwise returns the
original. Adding a source means implementing `ExchangeRateProvider`; the stored listing price is never
touched.

## Native push setup (not testable from this repository alone)

The web repo uses `@capacitor-firebase/messaging` (returns an **FCM** token on both platforms; the plain
`@capacitor/push-notifications` plugin returns a raw APNs token on iOS, which FCM cannot address).

1. Firebase console: create/choose the project; add Android app `com.hudhudnest.app` and iOS app.
2. Android: put `google-services.json` in `android/app/` (the build applies the plugin only when it is
   there). Not committed.
3. iOS: upload an APNs auth key (.p8) in Firebase; add `GoogleService-Info.plist` to the Xcode target;
   enable the **Push Notifications** and **Background Modes -> Remote notifications** capabilities for the
   App ID and profile; add the Firebase iOS SDK per the plugin's SPM instructions.
4. API host: set `Push__Fcm__Enabled=true`, `Push__Fcm__ProjectId`, and the service-account JSON secret.

## Rollout

1. Merge API PR; deploy to **Staging**. The migration runs first (additive).
2. On Staging: sign up, change language/currency from two clients, confirm a notification arrives in the
   recipient's language; register a real device and confirm a push (needs step 4 above on Staging).
3. User-acceptance on Staging (web, Android build, iOS TestFlight build).
4. Production only after the project's existing gates pass and with explicit approval; set the `Push__*`
   secrets last, so push turns on only after the rest is verified.

## Remaining work

* Security-alert emails are English-only; localizing them needs the account looked up by email in the
  background worker and four translated templates.
* No exchange-rate source (by decision); display currency has no visible effect yet beyond being stored.
* Real-device push (Android/iOS) and DB-backed integration tests were not run in this environment.
