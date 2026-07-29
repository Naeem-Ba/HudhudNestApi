EXPLAIN (ANALYZE, BUFFERS, VERBOSE, SETTINGS, FORMAT JSON)
SELECT p."Id", p."Title", p."City", p."ColdRent", p."Rooms", p."Area", p."CreatedAt"
FROM "Properties" p
WHERE NOT p."IsDeleted"
  AND p."IsPublished"
  AND p."CountryCode" = 'SY'
  AND p."City" ILIKE '%Damascus%'
  AND p."ListingType" = 'ForRent'
  AND p."ColdRent" BETWEEN 500 AND 2500
  AND p."Rooms" BETWEEN 2 AND 5
  AND p."Area" BETWEEN 50 AND 180
ORDER BY p."CreatedAt" DESC
LIMIT 20;
