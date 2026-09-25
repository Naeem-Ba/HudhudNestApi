# Multi-channel phone OTP: implementation report (2026-09-24)

Backend PR: `feature/multi-channel-otp` (API). Frontend PR: `feature/multi-channel-otp-ui` (app).
Design, configuration and operations are in [phone-password-authentication-and-reverification.md](../phone-password-authentication-and-reverification.md#otp-channels); this file is the status report.

Phone number = user identity. Channel = how one challenge's code is delivered. Changing the channel never creates a user.

## Implemented

* `OtpChannel` (`Sms`, `Telegram`, `WhatsApp`) on `PhoneOtpChallenge` with `ProviderRequestId`; additive, reversible migration `AddOtpChannelToPhoneOtpChallenges` (existing rows read as `Sms`).
* `IOtpProvider` + `OtpChannelService` (availability, recommendation, dispatch, logging, metrics). `SmsOtpProvider` wraps the existing `ISmsService`, so every SMS provider (Twilio, D7, Unimatrix, generic HTTP, console) keeps working unchanged.
* `TelegramGatewayOtpProvider`: official Gateway API, our own code via `sendVerificationMessage`, token in a Bearer header, timeout, tolerant response parsing.
* `WhatsAppCloudOtpProvider`: official Cloud API, approved AUTHENTICATION template. Off by default.
* `POST /api/auth/phone/channels`: channels for a number, with reason, recommendation and the configured unavailable country codes. WhatsApp is unavailable by default for +963, +53, +98, +850 (Meta's eligibility list).
* Optional `channel` on every send and verify endpoint; a code is only accepted for its own channel (wrong channel = generic `OTP_INVALID`, no attempt burnt); no channel = old behaviour (SMS).
* Shared hourly limit per number and purpose across channels (it already counted challenges, not SMS).
* Anti-enumeration: unreachable recipients keep the generic answer; availability and refusal depend on prefix and configuration only.
* Startup validation in Production when a channel is enabled without credentials; console stand-ins in Development/Testing/CI; Staging smoke suite never reaches Telegram/WhatsApp.
* Metrics `auth.otp.send` and `auth.otp.verification` (channel, outcome), structured logs with masked numbers.
* Frontend: shared picker and fallback components, `OtpChannelState`, all four phone pages, ar/en/de, error mapping, unavailable-channel text and fixed country list, manual fallback.

## Not implemented (and why)

* **Telegram delivery-report callback** (`X-Request-Timestamp` / `X-Request-Signature`): optional, would add an inbound endpoint; not needed to deliver or verify a code. Documented how to add it.
* **`checkSendAbility` and `checkVerificationStatus`**: the first is billed and reveals whether a number has Telegram; the second is unnecessary because verification is local.
* **Country label on metrics**: unbounded tag set; channel and outcome only.
* **Server-side automatic fallback**: deliberately user-initiated.
* **Real-account verification** of Telegram Gateway and WhatsApp (see below): needs credentials only the owner can create.

## Tests

| Kind | New | Suite result on this branch |
|---|---|---|
| Backend unit (Infrastructure: Telegram, WhatsApp, availability, service) | 43 | Infrastructure 127 passed |
| Backend domain | 1 | Auth 262 passed |
| Backend integration (real PostgreSQL) | 24 workflow + 5 registration | Integration 452 passed, 0 failed |
| Architecture (public endpoint allow-list entry) | 0 new, 1 entry | Architecture 164 passed |
| Frontend unit and component | 41 | 476 passed |
| E2E (Playwright, stubbed backend, real Angular build) | 6 | 6 passed against a local `ng serve` |
| Security-focused (no code/token/full number in logs, generic answers, channel binding, replay, shared limit) | included above | |

Fail-first proof: with the channel check in `ValidateAndReserveAsync` disabled, `ACodeIsOnlyAcceptedForItsOwnChannel_...` fails; restored afterwards.
Other checks: `dotnet format whitespace` clean; frontend `typecheck`, `typecheck:strict`, `check:i18n` (1541 keys, ar/en/de parity), `audit:frontend`, and a production build all pass.

## Manual and real-world tests

| Test | Status |
|---|---|
| Telegram self-test (own number, free) | **PASS** on 2026-09-24: local API, real Gateway token, one `registration/send-otp` with `channel=Telegram` to the owner's own German number; the 6-digit code arrived in the Telegram app. Code entry and verification were not exercised in this run |
| Telegram, another number in Staging | NOT TESTED |
| Telegram, German number | PASS (same run as the self-test: the owner's number is German) |
| Telegram, Syrian number | NOT TESTED (2026-09-25): Gateway answered HTTP 200 with error `BALANCE_NOT_ENOUGH` (sending to a number other than the account's own is billed), so coverage of Syria is still unknown. The adapter classified it as a provider failure (`OTP_PROVIDER_UNAVAILABLE`, `provider_unavailable`) and logged it without secrets, as designed |
| SMS | PASS on 2026-09-23 with Twilio to a German number, before this change; the SMS path is unchanged and covered by the tests above; not re-run afterwards |
| SMS to Syria (Unimatrix) | provider reported Delivered, **no message arrived on the phone**; unresolved |
| WhatsApp (any) | NOT TESTED: needs a Meta Business account and an approved template |
| Staging | NOT TESTED: no Staging access from the development environment |
| RTL / German layout | checked on screenshots at 1000 px and 375 px (ar, en, de) |

## Risks and open items

1. Telegram publishes no error catalogue: `PHONE_NUMBER_*` / `USER_*` = recipient unreachable, everything else = provider failure. Both need a real test.
2. Syrian coverage of Telegram Gateway is unknown. Do not enable it in Production for Syria on the strength of the API accepting a request; confirm delivery on a real Syrian phone.
3. The WhatsApp payload (body plus copy-code button parameter, error 131026 as undeliverable) follows Meta's documentation but is untested against a real account.
4. A provider outage is reported only for eligible numbers (as `SMS_FAILED` always was), so it is a small residual enumeration signal; recipient-specific failures are not.
5. The 3-per-hour limit is per number and purpose: a fallback consumes one of the three.
6. Set `SmsProvider:AllowedCountryCodes` in every deployed environment (unchanged advice; Telegram and WhatsApp sends are covered by it too).

## Deployment

1. Deploy the API first (backward compatible: without the new keys nothing changes; apply the migration).
2. Deploy the app. With only SMS available the pages look and behave as before.
3. To enable Telegram: set `OtpChannels__Telegram__Enabled=true` and `TelegramGateway__ApiToken` (secret) in the environment, restart, run the self-test, then a Syrian-number test, and only then set `OtpChannels__RecommendedByCountryCode__963=Telegram`.
4. Disable any channel by setting its `Enabled` to `false` and restarting; no code deployment is needed.
