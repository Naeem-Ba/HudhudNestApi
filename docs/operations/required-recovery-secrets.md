# Recovery secrets and external controls

Configure values in protected GitHub environments; never commit values.

| Name | Scope |
|---|---|
| `PRODUCTION_DATABASE_URL` | Preferred source backup connection; use a least-privilege backup account where provider capabilities permit |
| `DATABASE_URL` | Backward-compatible fallback for `PRODUCTION_DATABASE_URL`; configure only one of these names |
| `BACKUP_ENCRYPTION_KEY` | High-entropy GPG passphrase stored separately from object storage credentials |
| `BACKUP_STORAGE_URI` | Private `s3://bucket/prefix` destination |
| `BACKUP_AWS_ACCESS_KEY_ID` | Least-privilege object storage identity |
| `BACKUP_AWS_SECRET_ACCESS_KEY` | Object storage credential |
| `BACKUP_AWS_REGION` | Optional region |
| `BACKUP_S3_ENDPOINT_URL` | Optional HTTPS S3-compatible endpoint |
| `BACKUP_S3_KMS_KEY_ID` | Required when SSE mode is `aws:kms` |
| `BACKUP_S3_SSE` | GitHub environment variable; `AES256` (default) or `aws:kms` |
| `RECOVERY_ALERT_WEBHOOK_URL` | Optional secure notification webhook |
| `RENDER_API_KEY` | Render token permitted to read deploys and trigger rollback for one service |
| `RENDER_SERVICE_ID` | Production `srv-...` identifier |
| `PRODUCTION_BASE_URL` | HTTPS API base used for post-rollback checks |
| `EdgeProxy__SharedSecret` (Render) / `EDGE_PROXY_SECRET` (Netlify) | Not a recovery secret, but a restore of the service must set it again: the same random value (>= 32 chars) in both, so per-visitor auth rate limits work behind the Netlify auth proxy (launch audit A4-001) |

Protect `production-recovery`, `production-release`, and `production-rollback` with required reviewers and restrict branch deployment to `main`/`master`. Configure the bucket with TLS-only policy, public-access block, versioning, access logs, encryption, credential rotation, and object lock/immutability where available. Configure Render to deploy only after `Production Deployment Gate` succeeds; Render considers skipped/neutral checks acceptable, so this repository's final check explicitly fails when recovery is not `success`.

## Application runtime secrets needed to bring a restored service back (names only)

The table above covers backup, restore and rollback. A restored database alone does not give a working service: the API refuses to start in Production without the following, and a few of them cannot be regenerated without losing data or logging everybody out. Set them in Render (API) and, where noted, Netlify. Never commit values.

| Name | Where | If it is lost |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Render | Point it at the restored database (session pooler or direct, port 5432; port 6543 is refused) |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Render | New key = every signed-in user is signed out; sessions stay valid only with the same key |
| `OtpSettings__SecretKey` | Render | Open OTP codes become invalid; users request a new one |
| `Security__PhoneLookupHmacKey` (Base64, at least 32 bytes) | Render | **Keep an offline copy, do not regenerate**: phone lookups compare keyed hashes stored in the database, so with a different key existing phone logins and lookups stop matching (the listing view counter reuses the same key). Production refuses to start without it |
| `Redis__ConnectionString` (and `RedisRateLimiting__ConnectionString` if separate) | Render | Rate-limit counters only; no data is lost |
| `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret` | Render | Images are not in the database backup (see "Images and files" in rpo-rto.md) |
| `Email__Resend__ApiKey` or `Email__Password` (SMTP) | Render | No confirmation or reset mail |
| `Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__FromNumber`, `SmsProvider__AllowedCountryCodes` | Render | No OTP SMS; the empty allow-list now stops the API from starting |
| `Push__Fcm__ServiceAccountJson` | Render | No push notifications (in-app notices still work) |
| `EdgeProxy__SharedSecret` / `EDGE_PROXY_SECRET` | Render and Netlify | Auth rate limits count the proxy IP instead of the visitor |
| Social login and publishing credentials (`SocialAuth__*`, `SocialDistribution__*`) | Render | The related feature stays off |
| Translation provider key (when enabled) | Render | Automatic translation stays off |
| DataProtection key ring | Database table `DataProtectionKeys` (optional certificate/key for wrapping) | Restored with the database; if a wrapping certificate is configured it must be restored with it |

## Where the recovery copy lives (custody)

Rule: one encrypted copy of every secret above and of `BACKUP_ENCRYPTION_KEY`, outside GitHub, Render and the backup bucket, so that losing any one platform never loses the key to the others. A password manager vault shared with a second trusted person fits (two people, so one lost account is not a lost key). The repository records only that this copy exists, who holds it, and the date of the last check:

| Item | Holder 1 | Holder 2 | Last restore check |
|---|---|---|---|
| Recovery vault (all names above + `BACKUP_ENCRYPTION_KEY`) | owner | _to be named_ | _never_ |

A backup whose encryption key exists only in GitHub is not a backup: the first check is to decrypt the newest stored archive with the vault copy of the key.
