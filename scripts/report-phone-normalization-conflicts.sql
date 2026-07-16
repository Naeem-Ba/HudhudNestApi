-- Run before enabling the unique normalized-phone index.
-- PhoneNumber may be encrypted; populate NormalizedPhoneNumber through the application backfill first.
SELECT "NormalizedPhoneNumber", COUNT(*) AS "AccountCount", ARRAY_AGG("Id") AS "UserIds"
FROM "Users"
WHERE "NormalizedPhoneNumber" IS NOT NULL
GROUP BY "NormalizedPhoneNumber"
HAVING COUNT(*) > 1;

SELECT "Id", "NormalizedPhoneNumber"
FROM "Users"
WHERE "NormalizedPhoneNumber" IS NOT NULL
  AND "NormalizedPhoneNumber" !~ '^\+[1-9][0-9]{7,14}$';
