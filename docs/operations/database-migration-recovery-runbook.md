# EF Core migration recovery runbook

## Policy

A `Down` method is not proof of safe production reversal. Existing migrations either create structures that may later contain data (conditionally reversible) or transform/drop data (irreversible/destructive). No historical migration is approved for automatic production downgrade. `analyze-migrations.sh` fails any new unapproved high-risk/destructive migration; baseline approval requires a reviewed recovery note and pre-deployment backup.

| Migration | Classification | Destructive / risky operation | Down quality | Rollback strategy | Data-loss risk | Pre-backup | Expand/contract |
|---|---|---|---|---|---|---|---|
| InitialCleanArchitecture | Conditional | Creates complete schema | Down drops all tables | Restore or abandon empty initial DB | Critical after writes | Yes | N/A |
| HashRefreshTokens | Irreversible | Token representation/data conversion | Cannot recover original tokens | Forward-fix or restore | High | Yes | Yes |
| AddOtpCodes | Conditional | New table | Down drops table/data | App rollback; leave table | Medium | Yes | Preferred |
| AddNotifications | Conditional | New table | Down drops notifications | App rollback; leave table | Medium | Yes | Preferred |
| RemoveUserIsAgent | Destructive | Drops index/column | Cannot recover values | Restore pre-migration DB | High | Required | Required |
| AddOtpCleanupAndRefreshTokenRotation | Conditional/high | Rotation/cleanup schema and SQL | Data created later is lost on Down | Forward-fix or restore | High | Required | Yes |
| AddPropertyGeoLocationPostGis | Conditional/high | Extension/spatial column/index | Down loses spatial data | App rollback/forward-fix | High | Required | Yes |
| AddPropertyTextSearchTrigramIndexes | Conditional/high | Extension/index SQL | Index removal is reversible, performance is not | Forward-fix | Low data / high availability | Required | Yes |
| AddSyrianMarketLookupsAndApplicationRole | Conditional/high | Lookup/role data and relationships | Down can delete populated lookup data | Restore/forward-fix | High | Required | Yes |
| AddSecurityHardeningAuditLogsAndEncryptedUserFields | Irreversible | Sensitive-field transformation/audit schema | Cannot safely reconstruct plaintext/old form | Forward-fix or restore | Critical | Required | Required |
| AddDataProtectionKeys | Conditional | Key table | Down destroys keys and may make data unreadable | Never drop; app rollback | Critical | Required | Yes |
| ExpandContactMessageIpAddress | Reversible before new writes | Widening column | Narrowing may reject/truncate new values | Forward-fix | Medium | Required | Yes |
| AddUserAccountsProfileProjection | Irreversible | SQL data projection/new ownership model | Down drops projected profile data | Restore or audited forward-fix | Critical | Required | Required |
| AddPhoneNumberLookupHash | Irreversible | Backfill/derived unique identity lookup | Old lookup behavior cannot be safely reconstructed | Forward-fix/restore | High | Required | Yes |
| RebindBusinessUserRelationshipsToUserAccounts | Irreversible | Foreign-key/key rebind | Down may not preserve relationship history | Restore | Critical | Required | Required |
| CutOverIdentityToApplicationUser | Destructive | Drops multiple legacy columns | Values cannot be recreated | Restore pre-migration DB | Critical | Required | Required |
| AddPhonePasswordAuthenticationAndReverification | Conditional/high | Auth schema/state/backfill | Down can discard auth state | App rollback/forward-fix; restore if corrupt | Critical | Required | Yes |

The authoritative file names and machine policy are in `ci/migration-risk-baseline.json`; the table uses shortened migration names for readability.

## Pre-deployment sequence

1. Run build/tests and `bash scripts/database/analyze-migrations.sh`.
2. Review generated SQL, lock duration, table size, backfill idempotency, old/new app compatibility, and `Down` data effects.
3. For any schema/backfill/connectivity/high-risk release, create and verify the fresh pre-deployment backup. Record current/target commit, current/target migration, checksum, and backup ID.
4. Perform expand first, resumable measured backfill second, and contract in a later approved release after no active version uses the old shape. Create another backup before contract.
5. Execute only through `HudhudNestApi.Migrator`; do not enable startup migration in horizontally scaled API containers.

## Failure decisions

### Transaction failed with no schema change

Confirm EF history and catalog are unchanged, preserve logs, stop the deployment, keep/redeploy the previous app, and validate database/health. Do not record the migration as applied manually.

### Partial application or ambiguous state

Stop traffic or enter maintenance, preserve the failed database and logs, inspect `__EFMigrationsHistory` and catalog, and do not retry blindly. Reproduce on a clone. Use a peer-reviewed idempotent forward-fix when data is intact and the database is consistent; otherwise restore to a new database.

### Completed destructive migration incompatible with old app

Stop traffic, preserve the affected database for forensics, restore the verified pre-deployment backup to a new database/instance, run all recovery validation, roll back to the known-good Render deploy, securely point it to the recovered DB, smoke test, restore traffic, and record RPO/RTO. Do not overwrite the affected database during initial recovery.

A forward-fix is allowed only when no data loss occurred, constraints/catalog are consistent, the fix passed on a clone, peer review is recorded, predicted recovery is faster than restore, and the change cannot compound irreversible damage.
