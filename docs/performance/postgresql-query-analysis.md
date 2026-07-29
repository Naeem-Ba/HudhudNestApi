# PostgreSQL query-analysis procedure

## Controlled evidence

The performance database starts PostgreSQL 16/PostGIS 3.4 with `shared_preload_libraries=pg_stat_statements`,
`pg_stat_statements.track=all`, and `track_io_timing=on`. The runner creates PostGIS, pg_trgm, and
pg_stat_statements, applies migrations, generates the declared dataset, runs `ANALYZE`, and resets statement
statistics immediately before the controlled workload.

`scripts/capture-pg-stat-statements.sh` exports normalized queries, calls, planning/execution time, rows, shared
hits/reads, temporary blocks, and WAL counters to JSON and CSV. SQL parameters are not exported. The slow-query
summary ranks the controlled workload by total execution time.

## Plans captured

`scripts/capture-query-plans.sh` executes `EXPLAIN (ANALYZE, BUFFERS, VERBOSE, SETTINGS, FORMAT JSON)` for:

- first property page;
- deep property page at offset 9000;
- property detail with owner;
- combined city/type/price/rooms/area filter;
- PostGIS radius search ordered by distance.

The analyzer traverses every node and records node types, actual/estimated rows, indexes, shared buffer hits/reads,
temporary blocks, sort methods, large sequential scans, cardinality error, and spatial-index use. It does not treat
every sequential scan as a defect: only scans crossing the declared row threshold fail automatically.

## Query-count and N+1 gate

Performance-only EF interception counts completed database commands and total command duration per HTTP request.
Current budgets are list <=3, detail <=5, and geographic search <=2 commands. This catches a query-per-image,
query-per-amenity, or query-per-review regression while allowing the current bounded split detail query.

## Index decisions

No index migration is included without measured before/after evidence. Candidate issues include deep offset cost,
leading-wildcard city/region predicates, filter/sort composites, and LATERAL main-image lookup. For any future index:

1. Preserve the before plan and workload summary.
2. State target query, selectivity, storage estimate, write amplification, migration/locking risk, and overlap.
3. Apply the migration only to the disposable performance environment first.
4. Rerun the identical seed/profile three times.
5. Store the after plan and median latency/buffer improvement.
6. Confirm result equivalence and update an approved plan baseline through review.

The GiST index `IX_Properties_GeoLocation` is mandatory for representative radius search. Its disappearance or loss
of use is release blocking.
