EXPLAIN (ANALYZE, BUFFERS, VERBOSE, SETTINGS, FORMAT JSON)
WITH nearest_candidates AS MATERIALIZED (
    SELECT p.*
    FROM "Properties" p
    WHERE NOT p."IsDeleted"
      AND p."IsPublished"
      AND (p."ExpiresAt" IS NULL OR p."ExpiresAt" > now())
      AND p."GeoLocation" IS NOT NULL
    ORDER BY p."GeoLocation" <->
             ST_SetSRID(ST_MakePoint(36.2765, 33.5138), 4326)::geography
    LIMIT 20
),
nearest AS (
    SELECT p.*
    FROM nearest_candidates p
    WHERE ST_DWithin(
            p."GeoLocation",
            ST_SetSRID(ST_MakePoint(36.2765, 33.5138), 4326)::geography,
            20000)
    ORDER BY p."GeoLocation" <->
             ST_SetSRID(ST_MakePoint(36.2765, 33.5138), 4326)::geography
)
SELECT p."Id", p."Title",
       ST_Distance(
         p."GeoLocation",
         ST_SetSRID(ST_MakePoint(36.2765, 33.5138), 4326)::geography) AS distance_meters
FROM nearest p
ORDER BY distance_meters, p."CreatedAt" DESC;
