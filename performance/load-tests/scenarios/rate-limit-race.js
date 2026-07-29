import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';
import { metricValue, performanceArtifacts } from '../lib/reporting.js';

const budgets = JSON.parse(open('../../performance-budgets.json'));
const baseUrl = (__ENV.PERF_BASE_URL || '').replace(/\/$/, '');
const requestCount = Number(__ENV.PERF_RATE_REQUEST_COUNT || 20);
const configuredLimit = Number(__ENV.PERF_RATE_CONFIGURED_LIMIT || 10);
const windowSeconds = Number(__ENV.PERF_RATE_WINDOW_SECONDS || 5);
const target = __ENV.PERF_RATE_TARGET || 'login';
const startedAtUtc = new Date().toISOString();

const allowed = new Counter('rate_limit_allowed');
const rejected = new Counter('rate_limit_rejected');
const unexpected = new Counter('rate_limit_unexpected');
const recovered = new Counter('rate_limit_recovered');
const serverErrors = new Rate('server_errors');
const api1 = new Counter('instance_api_1');
const api2 = new Counter('instance_api_2');

export const options = {
  summaryTrendStats: ['min', 'p(50)', 'avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    distributed_rate_limit: {
      executor: 'per-vu-iterations',
      vus: requestCount,
      iterations: 1,
      maxDuration: '20s',
      exec: 'race'
    },
    window_recovery: {
      executor: 'shared-iterations',
      vus: 1,
      iterations: 1,
      startTime: `${windowSeconds * 2 + 3}s`,
      maxDuration: '15s',
      exec: 'recovery'
    }
  },
  thresholds: {
    rate_limit_allowed: [`count==${configuredLimit}`],
    rate_limit_rejected: [`count==${requestCount - configuredLimit}`],
    rate_limit_unexpected: ['count==0'],
    rate_limit_recovered: ['count==1'],
    server_errors: ['rate==0'],
    instance_api_1: ['count>0'],
    instance_api_2: ['count>0']
  }
};

function jsonPost(path, body) {
  return http.post(`${baseUrl}${path}`, JSON.stringify(body), {
    headers: { 'Content-Type': 'application/json' }
  });
}

function limitedAttempt() {
  switch (target) {
    case 'login':
      return jsonPost('/api/auth/phone/login', {
        phoneNumber: `${__ENV.PERF_TEST_PHONE_PREFIX}999999`,
        password: 'InvalidPerformancePassword!'
      });
    case 'registration':
      return jsonPost('/api/auth/register', {});
    case 'otp-request':
      return jsonPost('/api/auth/phone/registration/send-otp', {
        phoneNumber: `${__ENV.PERF_TEST_PHONE_PREFIX}888888`
      });
    case 'otp-verify':
      return jsonPost('/api/auth/phone/registration/verify', {
        challengeId: '00000000-0000-0000-0000-000000000000',
        code: '000000',
        password: 'InvalidPerformancePassword!',
        firstName: 'Performance',
        lastName: 'RateLimit'
      });
    case 'refresh':
      return jsonPost('/api/auth/refresh', { refreshToken: 'invalid-performance-token' });
    case 'public-search':
      return http.get(`${baseUrl}/api/properties?page=1&pageSize=5`);
    case 'geo-search':
      return http.get(`${baseUrl}/api/properties/geo-search?latitude=33.5138&longitude=36.2765&radiusKm=20&page=1&pageSize=5`);
    default:
      throw new Error(`Unsupported PERF_RATE_TARGET: ${target}`);
  }
}

function recordInstance(response) {
  const instance = response.headers['X-Instance-Id'];
  if (instance === 'api-1') api1.add(1);
  if (instance === 'api-2') api2.add(1);
  serverErrors.add(response.status >= 500);
}

export function setup() {
  if (!baseUrl || !['Local', 'CI', 'Performance', 'Staging'].includes(__ENV.PERF_ENVIRONMENT))
    throw new Error('Safe PERF_BASE_URL and PERF_ENVIRONMENT are required.');
  const now = Date.now();
  const windowMilliseconds = windowSeconds * 1000;
  const raceAt = (Math.floor(now / windowMilliseconds) + 1) * windowMilliseconds + 500;
  return { raceAt };
}

export function race(data) {
  const waitSeconds = Math.max(0, (data.raceAt - Date.now()) / 1000);
  if (waitSeconds > 0) sleep(waitSeconds);
  const response = limitedAttempt();
  recordInstance(response);
  if (response.status === 429) rejected.add(1);
  else if (response.status < 500) allowed.add(1);
  else unexpected.add(1);
  check(response, { 'rate-limit race response is expected': () =>
    response.status < 500 });
}

export function recovery() {
  const response = limitedAttempt();
  recordInstance(response);
  const acceptedAfterWindow = response.status !== 429 && response.status < 500;
  if (acceptedAfterWindow) recovered.add(1);
  check(response, { 'rate-limit window recovers': () => acceptedAfterWindow });
}

export function handleSummary(data) {
  const allowedCount = metricValue(data, 'rate_limit_allowed', 'count');
  const rejectedCount = metricValue(data, 'rate_limit_rejected', 'count');
  const measuredOvershoot = Math.max(0, allowedCount - configuredLimit);
  return performanceArtifacts(data, `rate-limit-${target}`, {
    startedAtUtc,
    target,
    configuredLimit,
    requestCount,
    allowedCount,
    rejectedCount,
    overshoot: measuredOvershoot,
    windowSeconds,
    recovered: metricValue(data, 'rate_limit_recovered', 'count') === 1
  });
}
