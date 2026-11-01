# Growth dashboard: metric definitions (growth engine P7 + P10)

Admin page `/admin/growth`, API `GET /api/admin/growth/overview?fromUtc=&toUtc=` (admins only). Window is
`[fromUtc, toUtc)` in UTC, default the last 30 days up to the end of today, at most 186 days. Counts only: the
response holds no names, e-mails, phone numbers or ids.

Source tables: `UserAccounts` (sign-ups by any method) and `GrowthEvents` (see DECISIONS D-10).

| Metric | Definition |
|---|---|
| New accounts | Accounts whose `CreatedAt` is in the window (e-mail, phone, Google and Apple; later deletions still count). |
| Listings published | `listing_published` events: first publication of a listing (re-publishing does not count again). |
| Search requests | `search_request_created` events. |
| Saved searches | `saved_search_created` events. |
| Favorites | `favorite_added` events: first time a user saves a listing (save, remove, save again counts once). |
| Contacts | `contact_started` events: first message from a non-owner about a listing, per conversation. |
| Proposals | `listing_proposed` events: an advertiser proposed a listing to a search request. |
| Referrals counted | `referral_qualified` events (rules in D-11). |
| Active users | Distinct accounts with at least one `user_active` day in the window. |
| Active day | A UTC day on which the account signed in or refreshed its session (written hourly by `UserActivityRollupHostedService` from refresh tokens). Opening the site without being signed in is not counted. |

## Funnel (accounts created in the window)
- **Registered**: new accounts in the window.
- **Activated**: did at least one of publish a listing, create a search request, create a saved search, save a
  favorite, or contact an advertiser within 30 days of sign-up.
- **Published** / **Contacted**: published a listing / contacted an advertiser within 30 days of sign-up.
Recent sign-ups have had less than 30 days, so the last weeks of a window always look lower.

## Campaigns (UTM)
- Rows are sign-ups (`user_registered` events in the window) grouped by `utm_source`, `utm_medium` and
  `utm_campaign` as first seen by the app within 30 days before sign-up. Same activation rules as the funnel.
- **Only e-mail sign-ups carry tags today.** Google/Apple and phone sign-ups are counted in New accounts but
  not in this table (known gap, D-10). "Untagged" counts e-mail sign-ups that arrived with no UTM tags.
- Top 50 rows by sign-ups. Tags are stored lower case, letters, digits, `-`, `_`, `.` only.
- The admin page has a link builder: pick the page, source, medium and campaign, copy the link.

## Retention (rolling)
- Cohort: accounts created in the window **on or after the first recorded active day** (the dashboard says the
  date; earlier accounts have no activity history and are left out rather than counted as lost).
- **Dn** = of the cohort accounts that are at least n days old, the share whose last active day is n or more
  days after their sign-up day. Shown for n = 1, 7 and 30, with the cohort size; small cohorts are noisy.
