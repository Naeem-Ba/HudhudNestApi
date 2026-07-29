import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { performanceArtifacts } from '../lib/reporting.js';

const budgets = JSON.parse(open('../../performance-budgets.json'));
const listBudget = budgets.scenarios.propertyList;
const detailBudget = budgets.scenarios.propertyDetail;
const geoBudget = budgets.scenarios.geographicSearch;
const baseUrl = (__ENV.PERF_BASE_URL || '').replace(/\/$/, '');
const duration = __ENV.PERF_TEST_DURATION || '30s';
const virtualUsers = Number(__ENV.PERF_VIRTUAL_USERS || 10);
const cacheMode = __ENV.PERF_CACHE_MODE || 'warm';
const startedAtUtc = new Date().toISOString();

const propertyListDuration = new Trend('property_list_duration', true);
const propertyDetailDuration = new Trend('property_detail_duration', true);
const geographicSearchDuration = new Trend('geographic_search_duration', true);
const propertyListDatabaseDuration = new Trend('property_list_database_duration', true);
const propertyDetailDatabaseDuration = new Trend('property_detail_database_duration', true);
const geographicSearchDatabaseDuration = new Trend('geographic_search_database_duration', true);
const steadyRequests = new Counter('steady_requests');
const functionalErrors = new Rate('functional_errors');
const serverErrors = new Rate('server_errors');
const api1 = new Counter('instance_api_1');
const api2 = new Counter('instance_api_2');

export const options = {
  discardResponseBodies: false,
  summaryTrendStats: ['min', 'p(50)', 'avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    warmup: {
      executor: 'constant-vus',
      vus: Math.max(1, Math.ceil(virtualUsers / 2)),
      duration: cacheMode === 'warm' ? '10s' : '1s',
      exec: 'warmup'
    },
    steady_state: {
      executor: 'constant-vus',
      vus: virtualUsers,
      duration,
      startTime: cacheMode === 'warm' ? '10s' : '1s',
      exec: 'steadyState'
    }
  },
  thresholds: {
    property_list_duration: [
      `p(95)<${listBudget.p95Milliseconds}`,
      `p(99)<${listBudget.p99Milliseconds}`
    ],
    property_detail_duration: [
      `p(95)<${detailBudget.p95Milliseconds}`,
      `p(99)<${detailBudget.p99Milliseconds}`
    ],
    geographic_search_duration: [
      `p(95)<${geoBudget.p95Milliseconds}`,
      `p(99)<${geoBudget.p99Milliseconds}`
    ],
    property_list_database_duration: [`p(95)<${listBudget.maximumDatabaseMilliseconds}`],
    property_detail_database_duration: [`p(95)<${detailBudget.maximumDatabaseMilliseconds}`],
    geographic_search_database_duration: [`p(95)<${geoBudget.maximumDatabaseMilliseconds}`],
    steady_requests: [`rate>${Math.min(listBudget.minimumRequestsPerSecond, detailBudget.minimumRequestsPerSecond)}`],
    functional_errors: [`rate<${budgets.global.maximumHttpErrorRate}`],
    server_errors: [`rate<${budgets.global.maximumServerErrorRate}`],
    checks: [`rate>${1 - budgets.global.maximumFailedCheckRate}`],
    instance_api_1: ['count>0'],
    instance_api_2: ['count>0']
  }
};

function recordInstance(response) {
  const instance = response.headers['X-Instance-Id'];
  if (instance === 'api-1') api1.add(1);
  if (instance === 'api-2') api2.add(1);
  serverErrors.add(response.status >= 500);
}

function requestParams(tags = {}) {
  const clientOctet = (((Math.max(1, __VU) - 1) * 31 + __ITER) % 250) + 1;
  return {
    tags,
    headers: { 'X-Performance-Client-Ip': `198.18.0.${clientOctet}` }
  };
}

function databaseDiagnostics(response, maximumQueryCount, durationMetric) {
  const count = Number(response.headers['X-Database-Command-Count']);
  const duration = Number(response.headers['X-Database-Duration-Ms']);
  if (Number.isFinite(duration)) durationMetric.add(duration);
  // A cache hit legitimately executes zero SQL commands. The upper bound is the
  // N+1 regression guard; requiring at least one command would reject warm-cache hits.
  return Number.isInteger(count) && count >= 0 && count <= maximumQueryCount &&
    Number.isFinite(duration) && duration >= 0;
}

function validPagedResponse(response) {
  if (response.status !== 200) return false;
  try {
    const body = response.json();
    return Array.isArray(body.Items) && Number.isInteger(body.TotalCount) &&
      Number.isInteger(body.Page) && Number.isInteger(body.PageSize);
  } catch (_) {
    return false;
  }
}

function list(path) {
  const response = http.get(`${baseUrl}${path}`,
    requestParams({ endpoint: 'property-list', cache: cacheMode }));
  propertyListDuration.add(response.timings.duration);
  steadyRequests.add(1);
  recordInstance(response);
  const databaseValid = databaseDiagnostics(response, listBudget.maximumQueryCount, propertyListDatabaseDuration);
  const valid = check(response, {
    'property list contract is valid': validPagedResponse,
    'property list query-count budget passes': () => databaseValid
  });
  functionalErrors.add(!valid);
  return response;
}

function detail(id) {
  const response = http.get(`${baseUrl}/api/properties/${id}`,
    requestParams({ endpoint: 'property-detail', cache: cacheMode }));
  propertyDetailDuration.add(response.timings.duration);
  steadyRequests.add(1);
  recordInstance(response);
  let valid = false;
  try {
    valid = response.status === 200 && response.json('Id') === id;
  } catch (_) {
    valid = false;
  }
  const databaseValid = databaseDiagnostics(
    response, detailBudget.maximumQueryCount, propertyDetailDatabaseDuration);
  check(response, {
    'property detail matches requested id': () => valid,
    'property detail query-count budget passes': () => databaseValid
  });
  functionalErrors.add(!valid || !databaseValid);
}

function geoSearch() {
  const radiusKm = 20;
  const response = http.get(
    `${baseUrl}/api/properties/geo-search?latitude=33.5138&longitude=36.2765&radiusKm=${radiusKm}&page=1&pageSize=20&listingType=ForRent`,
    requestParams({ endpoint: 'geographic-search', cache: cacheMode }));
  geographicSearchDuration.add(response.timings.duration);
  steadyRequests.add(1);
  recordInstance(response);
  let valid = false;
  try {
    const body = response.json();
    valid = response.status === 200 && Array.isArray(body.Items) &&
      body.Items.every((item) => item.DistanceMeters <= radiusKm * 1000 + 1);
  } catch (_) {
    valid = false;
  }
  const databaseValid = databaseDiagnostics(
    response, geoBudget.maximumQueryCount, geographicSearchDatabaseDuration);
  check(response, {
    'geographic results respect radius': () => valid,
    'geographic query-count budget passes': () => databaseValid
  });
  functionalErrors.add(!valid || !databaseValid);
}

export function setup() {
  if (!baseUrl || !['Local', 'CI', 'Performance', 'Staging'].includes(__ENV.PERF_ENVIRONMENT)) {
    throw new Error('Safe PERF_BASE_URL and PERF_ENVIRONMENT are required.');
  }
  const response = http.get(`${baseUrl}/api/properties?page=1&pageSize=20`);
  if (!validPagedResponse(response) || response.json('Items').length === 0) {
    throw new Error(
      `The performance dataset is missing or the property-list contract is invalid. ` +
      `status=${response.status}, body=${response.body.slice(0, 500)}`);
  }
  return { propertyIds: response.json('Items').map((item) => item.Id) };
}

export function warmup(data) {
  const id = data.propertyIds[__ITER % data.propertyIds.length];
  http.get(`${baseUrl}/api/properties?page=1&pageSize=20`, requestParams());
  http.get(`${baseUrl}/api/properties/${id}`, requestParams());
  http.get(`${baseUrl}/api/properties/geo-search?latitude=33.5138&longitude=36.2765&radiusKm=20&page=1&pageSize=20`, requestParams());
  sleep(0.1);
}

export function steadyState(data) {
  const choice = Math.random();
  const id = data.propertyIds[(__VU + __ITER) % data.propertyIds.length];
  if (choice < 0.35) list('/api/properties?page=1&pageSize=20&sortBy=CreatedAt&sortDescending=true');
  else if (choice < 0.50) list('/api/properties?page=50&pageSize=20');
  else if (choice < 0.60) list('/api/properties?page=450&pageSize=20');
  else if (choice < 0.72) list('/api/properties?city=Damascus&listingType=ForRent&minRooms=2&maxRooms=5&page=1&pageSize=20');
  else if (choice < 0.87) detail(id);
  else geoSearch();
  sleep(0.05);
}

export function handleSummary(data) {
  return performanceArtifacts(data, `browse-${cacheMode}`, {
    startedAtUtc,
    cacheMode,
    workload: '35% first page, 15% middle page, 10% deep page, 12% combined filters, 15% detail, 13% geo'
  });
}
