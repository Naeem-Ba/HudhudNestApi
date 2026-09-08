-- Finding F4 (docs/DATABASE-PRODUCTION-READINESS.md) pre-deployment audit.
--
-- Read-only. Reports how many existing Properties rows would violate the new
-- "nullable, but no zero/negative value" CHECK constraints the
-- AddPropertyNumericConstraints migration adds on Area, Rooms, ColdRent,
-- WarmRent, and PurchasePrice. Every one of these five columns stays
-- nullable (Area is legitimately optional except for Land listings --
-- confirmed in CreatePropertyCommandValidator.cs -- and the three price
-- columns are legitimately null depending on ListingType), so this audit
-- only ever needs to find non-null values that are <= 0.
--
-- Run this against Staging/Production BEFORE applying the migration there.
-- A non-zero count in any row below means the migration WILL fail (Postgres
-- refuses to add a CHECK constraint any existing row violates) -- which is
-- the intended, safe behavior, not a bug: the migration does not delete or
-- rewrite any row itself. Resolve every flagged row first (see
-- docs/DATABASE-PRODUCTION-READINESS.md Finding F4 for the recommended
-- correction options), then re-run this script until every count is 0.

SELECT
    (SELECT count(*) FROM "Properties" WHERE "Area" IS NOT NULL AND "Area" <= 0)              AS area_zero_or_negative,
    (SELECT count(*) FROM "Properties" WHERE "Rooms" IS NOT NULL AND "Rooms" <= 0)             AS rooms_zero_or_negative,
    (SELECT count(*) FROM "Properties" WHERE "ColdRent" IS NOT NULL AND "ColdRent" <= 0)       AS coldrent_zero_or_negative,
    (SELECT count(*) FROM "Properties" WHERE "WarmRent" IS NOT NULL AND "WarmRent" <= 0)       AS warmrent_zero_or_negative,
    (SELECT count(*) FROM "Properties" WHERE "PurchasePrice" IS NOT NULL AND "PurchasePrice" <= 0) AS purchaseprice_zero_or_negative;

-- To see the actual offending rows for any non-zero column above, e.g. Area:
--   SELECT "Id", "Area" FROM "Properties" WHERE "Area" IS NOT NULL AND "Area" <= 0;
