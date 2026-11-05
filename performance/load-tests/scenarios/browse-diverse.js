// Advisory (NOT part of the Production Gate): the same read workload as browse.js, but every
// request carries a cache key that has (almost) never been seen before, so the server's output
// cache cannot answer it and the request measures the real database/query path.
//
// Why this exists: browse.js repeats ~10 distinct URLs for the whole run, so since API output
// caching landed (#254/#273) its p95 mostly measures cache hits (single-digit ms). The approved
// baseline was captured before caching and measures the uncached path (hundreds of ms). The two
// numbers share a metric name and a unit but not a workload; this scenario gives the cached-path
// vs. database-path split a stable, separately-baselined home.
//
//  * list URLs   : `minArea=<tiny unique value>` is a no-op filter (all areas are >= 1 m2) that
//                  makes the vary-by-query cache key unique.
//  * detail URLs : ids are drawn at random from a pool (idPoolPages x 100 ids) so the 60 s TTL
//                  and the id spread keep the hit ratio low.
//  * geo URLs    : the centre is jittered by up to ~100 m, which keeps the same plan/result size
//                  but changes the cache key.
//
// Output: /artifacts/performance/study/browse-diverse-<runId>.json plus a STUDY_SUMMARY line on stdout.
// It is deliberately not written next to the gate's k6 results.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const baseUrl = (__ENV.PERF_BASE_URL || '').replace(/\/$/, '');
const duration = __ENV.PERF_TEST_DURATION || '30s';
const virtualUsers = Number(__ENV.PERF_VIRTUAL_USERS || 10);
const idPoolPages = Number(__ENV.PERF_ID_POOL_PAGES || 40);
const startedAtUtc = new Date().toISOString();

const propertyListDuration = new Trend('property_list_duration', true);
const propertyDetailDuration = new Trend('property_detail_duration', true);
const geographicSearchDuration = new Trend('geographic_search_duration', true);
const steadyDuration = new Trend('steady_duration', true);
const steadyRequests = new Counter('steady_requests');
const serverErrors = new Rate('server_errors');
const functionalErrors = new Rate('functional_errors');
const dbCommands = new Trend('database_commands_per_request');
const servedWithoutDatabase = new Rate('served_without_database');
const api1 = new Counter('instance_api_1');
const api2 = new Counter('instance_api_2');

export const options = {
  discardResponseBodies: false,
  summaryTrendStats: ['min', 'p(50)', 'avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    warmup: {
      executor: 'constant-vus',
      vus: Math.max(1, Math.ceil(virtualUsers / 2)),
      duration: '5s',
      exec: 'warmup'
    },
    steady_state: {
      executor: 'constant-vus',
      vus: virtualUsers,
      duration,
      startTime: '5s',
      exec: 'steadyState'
    }
  }
};

let counter = 0;
function unique() {
  counter += 1;
  // __VU keeps keys unique across virtual users; Math.random across runs.
  return `${(__VU * 1e-4 + counter * 1e-7 + Math.random() * 1e-9).toFixed(10)}`;
}

function params(tags) {
  const octet = (((Math.max(1, __VU) - 1) * 31 + __ITER) % 250) + 1;
  return { tags, headers: { 'X-Performance-Client-Ip': `198.18.0.${octet}` } };
}

function record(response) {
  const instance = response.headers['X-Instance-Id'];
  if (instance === 'api-1') api1.add(1);
  if (instance === 'api-2') api2.add(1);
  serverErrors.add(response.status >= 500);
  steadyRequests.add(1);
  steadyDuration.add(response.timings.duration);
  const commands = Number(response.headers['X-Database-Command-Count']);
  if (Number.isFinite(commands)) { dbCommands.add(commands); servedWithoutDatabase.add(commands === 0); }
}

function list(path, withUniqueKey = true) {
  const sep = path.includes('?') ? '&' : '?';
  const url = `${baseUrl}${path}${withUniqueKey ? `${sep}minArea=${unique()}` : ''}`;
  const response = http.get(url, params({ endpoint: 'property-list' }));
  propertyListDuration.add(response.timings.duration);
  record(response);
  let ok = false;
  try {
    const body = response.json();
    ok = response.status === 200 && Array.isArray(body.items) && Number.isInteger(body.totalCount);
  } catch (_) { ok = false; }
  check(response, { 'property list contract is valid': () => ok });
  functionalErrors.add(!ok);
}

function detail(id) {
  const response = http.get(`${baseUrl}/api/properties/${id}`, params({ endpoint: 'property-detail' }));
  propertyDetailDuration.add(response.timings.duration);
  record(response);
  let ok = false;
  try { ok = response.status === 200 && response.json('id') === id; } catch (_) { ok = false; }
  check(response, { 'property detail matches requested id': () => ok });
  functionalErrors.add(!ok);
}

function geoSearch() {
  const radiusKm = 20;
  const lat = (33.5138 + (Math.random() - 0.5) * 0.002).toFixed(7);
  const lon = (36.2765 + (Math.random() - 0.5) * 0.002).toFixed(7);
  const response = http.get(
    `${baseUrl}/api/properties/geo-search?latitude=${lat}&longitude=${lon}&radiusKm=${radiusKm}&page=1&pageSize=20&listingType=ForRent`,
    params({ endpoint: 'geographic-search' }));
  geographicSearchDuration.add(response.timings.duration);
  record(response);
  let ok = false;
  try {
    const body = response.json();
    ok = response.status === 200 && Array.isArray(body.items) &&
      body.items.every((item) => item.distanceMeters <= radiusKm * 1000 + 250);
  } catch (_) { ok = false; }
  check(response, { 'geographic results respect radius': () => ok });
  functionalErrors.add(!ok);
}

export function setup() {
  if (!baseUrl || !['Local', 'CI', 'Performance', 'Staging'].includes(__ENV.PERF_ENVIRONMENT)) {
    throw new Error('Safe PERF_BASE_URL and PERF_ENVIRONMENT are required.');
  }
  const ids = [];
  for (let page = 1; page <= idPoolPages; page += 1) {
    const response = http.get(`${baseUrl}/api/properties?page=${page}&pageSize=100`);
    if (response.status !== 200) throw new Error(`id pool page ${page} failed: ${response.status}`);
    for (const item of response.json('items')) ids.push(item.id);
  }
  if (ids.length === 0) throw new Error('The performance dataset is missing.');
  return { propertyIds: ids };
}

export function warmup(data) {
  const id = data.propertyIds[Math.floor(Math.random() * data.propertyIds.length)];
  http.get(`${baseUrl}/api/properties?page=1&pageSize=20&minArea=${unique()}`, params({}));
  http.get(`${baseUrl}/api/properties/${id}`, params({}));
  sleep(0.1);
}

export function steadyState(data) {
  const choice = Math.random();
  const id = data.propertyIds[Math.floor(Math.random() * data.propertyIds.length)];
  // Same mix as browse.js.
  if (choice < 0.35) list('/api/properties?page=1&pageSize=20&sortBy=CreatedAt&sortDescending=true');
  else if (choice < 0.50) list('/api/properties?page=50&pageSize=20');
  else if (choice < 0.60) list('/api/properties?page=450&pageSize=20');
  else if (choice < 0.72) list('/api/properties?city=Damascus&listingType=ForRent&minRooms=2&maxRooms=5&page=1&pageSize=20');
  else if (choice < 0.87) detail(id);
  else geoSearch();
  sleep(0.05);
}

export function handleSummary(data) {
  // Written to a study directory, never to .../k6/, so aggregate.py and compare.py (the gate)
  // cannot see this advisory scenario. `steady_duration` counts steady-state requests only
  // (warm-up requests never reach record()).
  const values = (name) => (data.metrics[name] && data.metrics[name].values) || {};
  const steady = values('steady_duration');
  const study = {
    scenario: 'browse-diverse',
    runId: __ENV.PERF_RUN_ID,
    startedAtUtc,
    completedAtUtc: new Date().toISOString(),
    virtualUsers,
    duration,
    steadyRequests: values('steady_requests').count || 0,
    p50: steady['p(50)'], p90: steady['p(90)'], p95: steady['p(95)'], p99: steady['p(99)'],
    max: steady.max, avg: steady.avg,
    requestsPerSecond: (values('steady_requests').count || 0) / Number(duration.replace(/s$/, '')),
    servedWithoutDatabase: values('served_without_database').rate,
    databaseCommandsPerRequestAvg: values('database_commands_per_request').avg,
    byEndpointP95: {
      list: values('property_list_duration')['p(95)'],
      detail: values('property_detail_duration')['p(95)'],
      geo: values('geographic_search_duration')['p(95)']
    },
    errors: { server: values('server_errors').rate, functional: values('functional_errors').rate },
    instances: { api1: values('instance_api_1').count, api2: values('instance_api_2').count }
  };
  return {
    [`/artifacts/performance/study/browse-diverse-${__ENV.PERF_RUN_ID || 'run'}.json`]: JSON.stringify(study, null, 2),
    stdout: `STUDY_SUMMARY ${JSON.stringify(study)}\n`
  };
}
