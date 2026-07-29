EXPLAIN (ANALYZE, BUFFERS, VERBOSE, SETTINGS, FORMAT JSON)
SELECT p."Id", p."Title", p."Description", ua."FirstName", ua."LastName"
FROM "Properties" p
JOIN "UserAccounts" ua ON ua."Id" = p."OwnerId"
WHERE p."Id" = (
    SELECT p2."Id" FROM "Properties" p2
    WHERE p2."IsPublished" AND NOT p2."IsDeleted"
    ORDER BY p2."CreatedAt" DESC LIMIT 1)
  AND p."IsPublished"
  AND NOT p."IsDeleted";
