# Current recovery capability assessment

Assessment date: 2026-07-28. Finding: M-10 (P0, production blocking).

## Repository facts

| Area | Confirmed repository state |
|---|---|
| Runtime | ASP.NET Core / .NET 8 |
| ORM and provider | EF Core 8.0.11 with Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11 |
| Database | PostgreSQL 16, pinned by `postgis/postgis:16-3.4` in the production gate |
| Spatial extension | PostGIS 3.4 in the pinned database image; startup/readiness checks execute a PostGIS query |
| Migrations | `PropertyApi.Infrastructure/Migrations`; history table is the EF default `__EFMigrationsHistory` |
| Migration execution | Separate `tools/PropertyApi.Migrator`; startup migration is disabled in production-like Compose |
| Health | `/health/live` and `/health/ready`; readiness covers PostgreSQL/PostGIS and Redis |
| Deployment evidence | Render is used externally. No `render.yaml`, registry publication workflow, production deploy job, or repository image-retention policy was present |
| Existing gate | Build, tests, vulnerability, coverage, production-like containers, migrations, PostGIS, Redis outage/recovery, and optional staging smoke |
| Notifications | GitHub workflow result only; no repository-configured incident service was present |

Critical data includes Identity tables, `UserAccounts`, `Properties`, `PropertyImages`, `PropertyAmenities`, `Favorites`, `Messages`, `RefreshTokens`, `Notifications`, `VisitRequests`, `PropertyReviews`, `Transactions`, `AuditLogs`, lookup tables, `DataProtectionKeys`, and EF migration history.

## Capability disposition

| Capability | Before this change | Repository control added | Provider/external prerequisite |
|---|---|---|---|
| Native custom-format backup | Missing | `backup-postgres.sh` uses matching PostgreSQL 16 tools and includes schema, data, sequences, indexes, constraints, functions, large objects, extension objects, and migration history | Production read-only/backup credential |
| Backup confidentiality | Missing | Client-side GPG AES-256 plus S3 SSE/KMS support; raw dumps are temporary | Private bucket, TLS endpoint, access logging, public-access block, versioning/object lock |
| Integrity verification | Missing | SHA-256, manifest schema, age, archive TOC, critical tables, migration history, and PostGIS checks | None after secrets are configured |
| Durable storage | Unverified | S3-compatible upload fails closed when required | Bucket and credentials must be provisioned; provider snapshots remain only an optional second layer |
| Retention | Missing | 48 hourly, 30 daily, 12 weekly, and 12 monthly selection/deletion | Bucket version lifecycle must agree with the policy |
| Actual isolated restore | Missing | PostgreSQL 16/PostGIS 3.4 disposable Compose target, hard production-host guards, `pg_restore`, SQL/EF/application validation, cleanup | Docker runner and production backup access |
| RPO/RTO | Missing | 60-minute RPO and 120-minute RTO are enforced in the drill report | GitHub scheduled jobs can be delayed; independent missed-schedule monitoring is still required for a hard RPO guarantee |
| Application rollback | Missing | Protected manual Render rollback by immutable deploy ID, readiness and API smoke checks, evidence JSON | Render API key/service ID, protected GitHub environment, auto-deploy pause during incident |
| Migration recovery | Informal | Every historical migration is classified; new risky migrations fail the gate; restore/forward-fix decision is documented | Human review and maintenance/traffic controls for incidents |
| Production gate | Recovery absent | Master/main production eligibility depends on a fresh backup, full restore, RPO/RTO, migration gate, and previous Render deploy | Render must be configured to deploy only after the named GitHub check passes |

## Current decision rule

Repository implementation is not equivalent to operational proof. A local end-to-end drill produced `restore-drill-evidence.json` with PASS on 2026-07-28, but M-10 remains production-blocking until a protected drill downloads from production object storage, the production secrets and protected environments are configured, storage controls are verified, rollback is exercised against Render, and the Render deployment trigger is confirmed to require `Production Deployment Gate`.
