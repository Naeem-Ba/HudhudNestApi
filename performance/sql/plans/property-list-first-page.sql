EXPLAIN (ANALYZE, BUFFERS, VERBOSE, SETTINGS, FORMAT JSON)
SELECT p."Id", p."Title", p."City", p."ListingType", p."CreatedAt"
FROM "Properties" p
WHERE NOT p."IsDeleted"
  AND p."IsPublished"
  AND (p."ExpiresAt" IS NULL OR p."ExpiresAt" > now())
ORDER BY p."CreatedAt" DESC
LIMIT 20;
