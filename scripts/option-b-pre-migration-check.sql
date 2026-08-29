-- ============================================================================
-- Option B pre-migration safety check (DATABASE ARCHITECTURE AUDIT REPORT, §20)
-- ============================================================================
-- Run this against staging/production BEFORE applying migration
-- 20260829114019_OptionBAuditCleanup. It is entirely read-only (SELECT only) --
-- it changes nothing. Every query should return 0 rows / a count of 0.
--
-- If any query returns rows, STOP and share the results before running the
-- migration -- that FK addition (or table drop) would either fail outright or
-- silently discard data.
-- ============================================================================

-- 1) UserAccounts.Id must always have a matching Users.Id (shared-PK 1:1).
--    A non-empty result here means the FK_UserAccounts_Users_Id migration step
--    will fail outright.
SELECT ua."Id" AS "OrphanUserAccountId"
FROM "UserAccounts" ua
LEFT JOIN "Users" u ON u."Id" = ua."Id"
WHERE u."Id" IS NULL;

-- 2) Agency.OwnerUserId must reference an existing UserAccounts row.
SELECT a."Id" AS "AgencyId", a."OwnerUserId"
FROM "Agencies" a
LEFT JOIN "UserAccounts" ua ON ua."Id" = a."OwnerUserId"
WHERE ua."Id" IS NULL;

-- 3) ServiceProvider.UserId must reference an existing UserAccounts row.
SELECT sp."Id" AS "ServiceProviderId", sp."UserId"
FROM "ServiceProviders" sp
LEFT JOIN "UserAccounts" ua ON ua."Id" = sp."UserId"
WHERE ua."Id" IS NULL;

-- 4) PhoneOtpChallenge.UserId, where set, must reference an existing Users row.
SELECT poc."Id" AS "PhoneOtpChallengeId", poc."UserId"
FROM "PhoneOtpChallenges" poc
LEFT JOIN "Users" u ON u."Id" = poc."UserId"
WHERE poc."UserId" IS NOT NULL AND u."Id" IS NULL;

-- 5) Transaction.PropertyId must reference an existing Properties row.
SELECT t."Id" AS "TransactionId", t."PropertyId"
FROM "Transactions" t
LEFT JOIN "Properties" p ON p."Id" = t."PropertyId"
WHERE p."Id" IS NULL;

-- 6) Transaction.PayerId must reference an existing UserAccounts row.
SELECT t."Id" AS "TransactionId", t."PayerId"
FROM "Transactions" t
LEFT JOIN "UserAccounts" ua ON ua."Id" = t."PayerId"
WHERE ua."Id" IS NULL;

-- 7) Transaction.ReceiverId must reference an existing UserAccounts row.
SELECT t."Id" AS "TransactionId", t."ReceiverId"
FROM "Transactions" t
LEFT JOIN "UserAccounts" ua ON ua."Id" = t."ReceiverId"
WHERE ua."Id" IS NULL;

-- 8) Transaction.BookingId, where set, must reference an existing VisitRequests row.
SELECT t."Id" AS "TransactionId", t."BookingId"
FROM "Transactions" t
LEFT JOIN "VisitRequests" vr ON vr."Id" = t."BookingId"
WHERE t."BookingId" IS NOT NULL AND vr."Id" IS NULL;

-- ============================================================================
-- Row counts for the tables the migration DROPS. These should be 0 (or, if
-- non-zero, contain only rows you recognize as staging/test fixtures -- the
-- audit found no production code path that writes to any of them).
-- ============================================================================

SELECT 'OtpCodes' AS "Table", COUNT(*) AS "RowCount" FROM "OtpCodes"
UNION ALL
SELECT 'RentalDetails', COUNT(*) FROM "RentalDetails"
UNION ALL
SELECT 'SaleDetails', COUNT(*) FROM "SaleDetails";

-- ============================================================================
-- The migration also renames ServiceReviewDocuments -> ServiceRequestDocuments
-- via DROP + CREATE (EF does not detect renames automatically), which loses
-- any existing rows. Given how recently the Services Marketplace feature
-- shipped, this is very likely empty everywhere except possibly a fresh
-- staging environment -- but check before applying to any environment that
-- might already have real uploads.
-- ============================================================================

SELECT COUNT(*) AS "ServiceReviewDocuments_RowCount" FROM "ServiceReviewDocuments";
