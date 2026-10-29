-- HudhudNest: read-only check before (or after) the migration NormalizeSchemaIntegrity.
-- Changes nothing. Run it on Staging first, then Production, with psql and the database URL:
--   psql "<database url>" -f scripts/database/normalize-schema-integrity-check.sql
-- Every line of the first result must show 0. Any other number names a rule that existing rows break;
-- the migration still succeeds in that case and leaves that one rule NOT VALID (see the second result).

SELECT rule, broken_rows FROM (
  SELECT 'ShortStayListings.OwnerId -> UserAccounts' AS rule, count(*) AS broken_rows FROM "ShortStayListings" t WHERE NOT EXISTS (SELECT 1 FROM "UserAccounts" u WHERE u."Id" = t."OwnerId")
  UNION ALL SELECT 'ShortStayListings.PropertyId -> Properties', count(*) FROM "ShortStayListings" t WHERE t."PropertyId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Properties" p WHERE p."Id" = t."PropertyId")
  UNION ALL SELECT 'ShortStayListings.GovernorateId -> Governorates', count(*) FROM "ShortStayListings" t WHERE t."GovernorateId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Governorates" g WHERE g."Id" = t."GovernorateId")
  UNION ALL SELECT 'ShortStayListings.DistrictId -> Districts', count(*) FROM "ShortStayListings" t WHERE t."DistrictId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Districts" d WHERE d."Id" = t."DistrictId")
  UNION ALL SELECT 'ShortStayListings.NeighborhoodId -> Neighborhoods', count(*) FROM "ShortStayListings" t WHERE t."NeighborhoodId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Neighborhoods" n WHERE n."Id" = t."NeighborhoodId")
  UNION ALL SELECT 'ShortStayBookings.GuestId -> UserAccounts', count(*) FROM "ShortStayBookings" t WHERE NOT EXISTS (SELECT 1 FROM "UserAccounts" u WHERE u."Id" = t."GuestId")
  UNION ALL SELECT 'ShortStayReviews.BookingId -> ShortStayBookings', count(*) FROM "ShortStayReviews" t WHERE NOT EXISTS (SELECT 1 FROM "ShortStayBookings" b WHERE b."Id" = t."BookingId")
  UNION ALL SELECT 'ShortStayReviews.ReviewerId -> UserAccounts', count(*) FROM "ShortStayReviews" t WHERE NOT EXISTS (SELECT 1 FROM "UserAccounts" u WHERE u."Id" = t."ReviewerId")
  UNION ALL SELECT 'ShortStayReviews.ShortStayListingId -> ShortStayListings', count(*) FROM "ShortStayReviews" t WHERE NOT EXISTS (SELECT 1 FROM "ShortStayListings" l WHERE l."Id" = t."ShortStayListingId")
  UNION ALL SELECT 'HostVerificationRecords.UserId -> UserAccounts', count(*) FROM "HostVerificationRecords" t WHERE NOT EXISTS (SELECT 1 FROM "UserAccounts" u WHERE u."Id" = t."UserId")
  UNION ALL SELECT 'ServiceReviews.ReviewerId -> UserAccounts', count(*) FROM "ServiceReviews" t WHERE NOT EXISTS (SELECT 1 FROM "UserAccounts" u WHERE u."Id" = t."ReviewerId")
  UNION ALL SELECT 'ServiceProviders.AgencyId -> Agencies', count(*) FROM "ServiceProviders" t WHERE t."AgencyId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Agencies" a WHERE a."Id" = t."AgencyId")
  UNION ALL SELECT 'Transactions.CurrencyId -> Currencies', count(*) FROM "Transactions" t WHERE NOT EXISTS (SELECT 1 FROM "Currencies" c WHERE c."Id" = t."CurrencyId")
  UNION ALL SELECT 'ValuationInquiries.PropertyTypeId -> PropertyTypes', count(*) FROM "ValuationInquiries" t WHERE t."PropertyTypeId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "PropertyTypes" p WHERE p."Id" = t."PropertyTypeId")
  UNION ALL SELECT 'SavedSearches.GovernorateId -> Governorates', count(*) FROM "SavedSearches" t WHERE t."GovernorateId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Governorates" g WHERE g."Id" = t."GovernorateId")
  UNION ALL SELECT 'SavedSearches.DistrictId -> Districts', count(*) FROM "SavedSearches" t WHERE t."DistrictId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Districts" d WHERE d."Id" = t."DistrictId")
  UNION ALL SELECT 'SavedSearches.NeighborhoodId -> Neighborhoods', count(*) FROM "SavedSearches" t WHERE t."NeighborhoodId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Neighborhoods" n WHERE n."Id" = t."NeighborhoodId")
  UNION ALL SELECT 'SavedSearches.PropertyTypeId -> PropertyTypes', count(*) FROM "SavedSearches" t WHERE t."PropertyTypeId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "PropertyTypes" p WHERE p."Id" = t."PropertyTypeId")
  UNION ALL SELECT 'ShortStayBookings CheckOut > CheckIn', count(*) FROM "ShortStayBookings" WHERE NOT ("CheckOut" > "CheckIn")
  UNION ALL SELECT 'ShortStayBookings amounts (0 <= deposit <= total, remaining = total - deposit)', count(*) FROM "ShortStayBookings" WHERE NOT ("DepositAmount" >= 0 AND "DepositAmount" <= "TotalAmount" AND "RemainingAmount" = "TotalAmount" - "DepositAmount")
  UNION ALL SELECT 'ShortStayReviews.Rating 1..5', count(*) FROM "ShortStayReviews" WHERE NOT ("Rating" BETWEEN 1 AND 5)
  UNION ALL SELECT 'PropertyReviews.Rating 1..5', count(*) FROM "PropertyReviews" WHERE NOT ("Rating" BETWEEN 1 AND 5)
  UNION ALL SELECT 'ServiceReviews.Rating 1..5', count(*) FROM "ServiceReviews" WHERE NOT ("Rating" BETWEEN 1 AND 5)
  UNION ALL SELECT 'UserRatings scores 1..5', count(*) FROM "UserRatings" WHERE NOT (coalesce("InformationAccuracy",1) BETWEEN 1 AND 5 AND "Credibility" BETWEEN 1 AND 5 AND "Safety" BETWEEN 1 AND 5 AND "ResponseSpeed" BETWEEN 1 AND 5 AND "Transparency" BETWEEN 1 AND 5 AND coalesce("Conduct",1) BETWEEN 1 AND 5)
) r ORDER BY broken_rows DESC, rule;

-- After the migration: constraints that stayed NOT VALID (expected: no rows).
SELECT conrelid::regclass AS table_name, conname AS constraint_name
FROM pg_constraint WHERE NOT convalidated AND connamespace = 'public'::regnamespace;

-- After fixing the rows a rule names, validate it by hand, for example:
--   ALTER TABLE "ShortStayListings" VALIDATE CONSTRAINT "FK_ShortStayListings_UserAccounts_OwnerId";

-- Information only (no constraint depends on it): the account flag is stored twice, on Users and on
-- UserAccounts, and DeleteUserCommandHandler sets both in one transaction. Expected: 0.
SELECT count(*) AS deleted_flag_drift
FROM "Users" u JOIN "UserAccounts" a ON a."Id" = u."Id"
WHERE u."IsDeleted" <> a."IsDeleted";
