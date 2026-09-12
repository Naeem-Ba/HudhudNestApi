import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { performanceArtifacts } from '../lib/reporting.js';

// Valuation remediation L2 — the module had zero load-test coverage (an explicit audit
// finding). Scope is deliberately limited to what an anonymous k6 script can actually exercise
// without a pre-provisioned privileged credential:
//   - Create Inquiry (POST /api/ValuationInquiries) — this single endpoint IS the Fast Path
//     AND Office Matching check: CreateValuationInquiryCommandHandler runs
//     GetComparableListingsQueryHandler synchronously and, only when it does not already
//     answer the request in full, runs OfficeMatchingService in the same request — there is no
//     separate "office matching" endpoint to call.
//   - Get Status (GET /api/ValuationInquiries/{id}/status) — repeated polling, the customer's
//     own read path.
// The Admin Office Statistics dashboard needs an Admin-role JWT this anonymous script cannot
// mint on its own (unlike auth-races.js's self-registration flow, roles are not
// self-service) — it is exercised ONLY when PERF_VALUATION_ADMIN_TOKEN is supplied, and
// skipped with a clear setup-time note otherwise, rather than silently omitted or faked.
// Expiry processing (ValuationInquiryExpiryHostedService's sweep) has no HTTP surface at all —
// it cannot be load-tested through k6; ValuationSlaEnforcementServiceTests' own
// RunSweepAsync_BacklogLargerThanOneBatch_DrainsMultipleBatchesInOneCall (Application.Tests)
// is this module's coverage for that batching behavior instead.
//
// Never executed in this remediation's own sandbox (no live deployed environment, no k6
// binary) — written to the same conventions as browse.js/auth-races.js so it is ready to run
// once one exists. Budget numbers in performance-budgets.json's own "valuation" section are
// REASONED, not measured (same "approved": false status the file already declares for every
// existing scenario) — see this remediation's own report for that caveat spelled out.

const budgets = JSON.parse(open('../../performance-budgets.json'));
const createBudget = budgets.scenarios.valuationCreateInquiry;
const statusBudget = budgets.scenarios.valuationGetStatus;
const adminBudget = budgets.scenarios.valuationAdminStatistics;
const baseUrl = (__ENV.PERF_BASE_URL || '').replace(/\/$/, '');
const duration = __ENV.PERF_TEST_DURATION || '30s';
const virtualUsers = Number(__ENV.PERF_VIRTUAL_USERS || 5);
const adminToken = __ENV.PERF_VALUATION_ADMIN_TOKEN || '';
const startedAtUtc = new Date().toISOString();

const createInquiryDuration = new Trend('valuation_create_inquiry_duration', true);
const getStatusDuration = new Trend('valuation_get_status_duration', true);
const adminStatisticsDuration = new Trend('valuation_admin_statistics_duration', true);
const createInquiryDatabaseDuration = new Trend('valuation_create_inquiry_database_duration', true);
const getStatusDatabaseDuration = new Trend('valuation_get_status_database_duration', true);
const steadyRequests = new Counter('steady_requests');
const functionalErrors = new Rate('functional_errors');
const serverErrors = new Rate('server_errors');
const rateLimited = new Counter('valuation_rate_limited_responses');

export const options = {
  discardResponseBodies: false,
  summaryTrendStats: ['min', 'p(50)', 'avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    steady_state: {
      executor: 'constant-vus',
      vus: virtualUsers,
      duration,
      exec: 'steadyState'
    }
  },
  thresholds: {
    valuation_create_inquiry_duration: [
      `p(95)<${createBudget.p95Milliseconds}`,
      `p(99)<${createBudget.p99Milliseconds}`
    ],
    valuation_get_status_duration: [
      `p(95)<${statusBudget.p95Milliseconds}`,
      `p(99)<${statusBudget.p99Milliseconds}`
    ],
    valuation_create_inquiry_database_duration: [`p(95)<${createBudget.maximumDatabaseMilliseconds}`],
    valuation_get_status_database_duration: [`p(95)<${statusBudget.maximumDatabaseMilliseconds}`],
    functional_errors: [`rate<${budgets.global.maximumHttpErrorRate}`],
    server_errors: [`rate<${budgets.global.maximumServerErrorRate}`],
    checks: [`rate>${1 - budgets.global.maximumFailedCheckRate}`]
  }
};

// Every VU/iteration gets its own spoofed client IP, same technique browse.js already uses —
// required here specifically because POST /api/ValuationInquiries is rate-limited to 10/hour
// per client (RedisRateLimitingDefaults["valuation-inquiries"]): without varying the apparent
// client, a sustained run would immediately start measuring 429s instead of real handler
// latency.
function requestParams(tags = {}) {
  const clientOctet = (((Math.max(1, __VU) - 1) * 31 + __ITER) % 250) + 1;
  return {
    tags,
    headers: {
      'Content-Type': 'application/json',
      'X-Performance-Client-Ip': `198.18.1.${clientOctet}`
    }
  };
}

function databaseDiagnostics(response, maximumQueryCount, durationMetric) {
  const count = Number(response.headers['X-Database-Command-Count']);
  const dbDuration = Number(response.headers['X-Database-Duration-Ms']);
  if (Number.isFinite(dbDuration)) durationMetric.add(dbDuration);
  return Number.isInteger(count) && count >= 0 && count <= maximumQueryCount &&
    Number.isFinite(dbDuration) && dbDuration >= 0;
}

function createInquiry(governorateId) {
  const payload = JSON.stringify({
    GovernorateId: governorateId,
    DistrictId: null,
    NeighborhoodId: null,
    PropertyTypeId: null,
    Area: null,
    Rooms: null,
    RequestType: 'ForSale'
  });

  const response = http.post(
    `${baseUrl}/api/ValuationInquiries`, payload,
    requestParams({ endpoint: 'valuation-create-inquiry' }));

  steadyRequests.add(1);
  serverErrors.add(response.status >= 500);

  if (response.status === 429) {
    rateLimited.add(1);
    return null;
  }

  createInquiryDuration.add(response.timings.duration);
  let inquiryId = null;
  let valid = false;
  try {
    const body = response.json();
    // RequiresOfficeValuation/OfficeMatchCount/FastPath are exactly the Fast-Path-vs-
    // Office-Path decision this single endpoint makes -- there is nothing further to call to
    // exercise "office matching" separately.
    valid = response.status === 201 &&
      typeof body.inquiryId === 'string' &&
      typeof body.requiresOfficeValuation === 'boolean' &&
      body.fastPath !== undefined;
    inquiryId = body.inquiryId;
  } catch (_) {
    valid = false;
  }

  const databaseValid = databaseDiagnostics(
    response, createBudget.maximumQueryCount, createInquiryDatabaseDuration);
  check(response, {
    'create inquiry contract is valid': () => valid,
    'create inquiry query-count budget passes': () => databaseValid
  });
  functionalErrors.add(!valid || !databaseValid);
  return inquiryId;
}

function getStatus(inquiryId) {
  const response = http.get(
    `${baseUrl}/api/ValuationInquiries/${inquiryId}/status`,
    requestParams({ endpoint: 'valuation-get-status' }));

  steadyRequests.add(1);
  serverErrors.add(response.status >= 500);
  getStatusDuration.add(response.timings.duration);

  let valid = false;
  try {
    const body = response.json();
    valid = response.status === 200 && typeof body.status === 'string' && Array.isArray(body.estimates);
  } catch (_) {
    valid = false;
  }

  const databaseValid = databaseDiagnostics(
    response, statusBudget.maximumQueryCount, getStatusDatabaseDuration);
  check(response, {
    'get status contract is valid': () => valid,
    'get status query-count budget passes': () => databaseValid
  });
  functionalErrors.add(!valid || !databaseValid);
}

function adminStatistics() {
  if (!adminToken) return; // see this file's own header comment.

  const response = http.get(
    `${baseUrl}/api/admin/valuation-offices/statistics`,
    { headers: { Authorization: `Bearer ${adminToken}` }, tags: { endpoint: 'valuation-admin-statistics' } });

  steadyRequests.add(1);
  serverErrors.add(response.status >= 500);
  adminStatisticsDuration.add(response.timings.duration);

  const valid = response.status === 200 && Array.isArray(response.json());
  check(response, { 'admin statistics contract is valid': () => valid });
  functionalErrors.add(!valid);
}

export function setup() {
  if (!baseUrl || !['Local', 'CI', 'Performance', 'Staging'].includes(__ENV.PERF_ENVIRONMENT)) {
    throw new Error('Safe PERF_BASE_URL and PERF_ENVIRONMENT are required.');
  }
  if (!__ENV.PERF_VALUATION_GOVERNORATE_ID) {
    throw new Error(
      'PERF_VALUATION_GOVERNORATE_ID is required: a real Governorate id from the target ' +
      'environment\'s own Lookups data, so every VU submits a valid inquiry.');
  }
  if (!adminToken) {
    console.warn(
      'PERF_VALUATION_ADMIN_TOKEN not set -- the admin-statistics check will be skipped this run.');
  }
  return { governorateId: Number(__ENV.PERF_VALUATION_GOVERNORATE_ID) };
}

export function steadyState(data) {
  const inquiryId = createInquiry(data.governorateId);
  if (inquiryId) {
    getStatus(inquiryId);
  }
  if (__ITER % 20 === 0) {
    adminStatistics();
  }
  sleep(0.2);
}

export function handleSummary(data) {
  return performanceArtifacts(data, 'valuation', {
    startedAtUtc,
    workload: 'Create Inquiry (Fast Path/Office Matching) + Get Status polling; ' +
      'Admin Statistics only when PERF_VALUATION_ADMIN_TOKEN is set'
  });
}
