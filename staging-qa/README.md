# staging-qa — synthetic data batch HUDHUDNEST-STAGING-QA-2026-10

Scripts that created and tested 100 synthetic accounts/listings on **Staging only** (2026-10-08).
Results: `../STAGING-TEST-REPORT.md`, `../STAGING-TEST-DATA.md`, `../staging-test-results.json`,
evidence in `../staging-test-evidence/`.

- `lib.mjs` — client, CSRF handling, synthetic PNGs, and the **safety gate** (aborts unless
  `/api/operational/build-info` proves Staging with isolation on).
- `generate.mjs` (accounts + listings + images, resumable), `tests.mjs` (functional, security,
  performance), `repro-main-image.mjs` (STG-001/002), `cleanup.mjs` (dry run unless `--yes`;
  only touches accounts whose last name carries the batch id), `build-report.mjs`.
- Secrets are never stored here. Provide them via a local file referenced by `STAGING_ENV_FILE`
  with `STAGING_SMOKE_FIXED_OTP`, `STAGING_SMOKE_PHONE_PREFIX`, `STAGING_SMOKE_CLEANUP_SECRET`,
  `STAGING_SMOKE_PASSWORD`. `state.json` (contains test phone numbers) is not committed.
