# Recovery secrets and external controls

Configure values in protected GitHub environments; never commit values.

| Name | Scope |
|---|---|
| `PRODUCTION_DATABASE_URL` | Source backup connection; use a least-privilege backup account where provider capabilities permit |
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

Protect `production-recovery`, `production-release`, and `production-rollback` with required reviewers and restrict branch deployment to `main`/`master`. Configure the bucket with TLS-only policy, public-access block, versioning, access logs, encryption, credential rotation, and object lock/immutability where available. Configure Render to deploy only after `Production Deployment Gate` succeeds; Render considers skipped/neutral checks acceptable, so this repository's final check explicitly fails when recovery is not `success`.
