import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { metricValue, performanceArtifacts } from '../lib/reporting.js';

const baseUrl = (__ENV.PERF_BASE_URL || '').replace(/\/$/, '');
const raceKind = __ENV.PERF_RACE_KIND || 'refresh';
const concurrency = Number(__ENV.PERF_RACE_CONCURRENCY || 2);
const phonePrefix = __ENV.PERF_TEST_PHONE_PREFIX || '';
const phoneSuffix = __ENV.PERF_RACE_PHONE_SUFFIX || '';
const password = __ENV.PERF_TEST_PASSWORD || '';
const fixedOtp = __ENV.PERF_FIXED_OTP || '';
const startedAtUtc = new Date().toISOString();

const raceSuccesses = new Counter('race_successes');
const raceRejections = new Counter('race_rejections');
const raceRequests = new Counter('race_requests');
const invariantFailures = new Counter('security_invariant_failures');
const serverErrors = new Rate('server_errors');
const raceDuration = new Trend('auth_race_duration', true);
const api1 = new Counter('instance_api_1');
const api2 = new Counter('instance_api_2');

export const options = {
  discardResponseBodies: false,
  summaryTrendStats: ['min', 'p(50)', 'avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    race: {
      executor: 'per-vu-iterations',
      vus: concurrency,
      iterations: 1,
      maxDuration: '30s'
    }
  },
  thresholds: {
    race_requests: [`count==${concurrency}`],
    race_successes: [raceKind === 'otp-invalid' ? 'count==0' : 'count==1'],
    race_rejections: [raceKind === 'otp-invalid' ? `count==${concurrency}` : `count==${concurrency - 1}`],
    security_invariant_failures: ['count==0'],
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

function recordInstance(response) {
  const instance = response.headers['X-Instance-Id'];
  if (instance === 'api-1') api1.add(1);
  if (instance === 'api-2') api2.add(1);
}

function sendChallenge(phone) {
  const response = jsonPost('/api/auth/phone/registration/send-otp', { phoneNumber: phone });
  // BUG-23: API responses use camelCase JSON keys since the B-11 migration
  // (Program.cs sets PropertyNamingPolicy = JsonNamingPolicy.CamelCase) —
  // reading 'ChallengeId' returned undefined on every real run.
  const challengeId = response.status === 200 ? response.json('challengeId') : null;
  if (!challengeId) {
    throw new Error(
      `Could not create an isolated OTP race challenge. ` +
      `status=${response.status}, body=${response.body.slice(0, 500)}`);
  }
  return challengeId;
}

function registrationPayload(challengeId, marker) {
  return {
    challengeId,
    code: fixedOtp,
    password,
    firstName: 'Performance',
    lastName: marker
  };
}

export function setup() {
  if (!baseUrl || !['Local', 'CI', 'Performance', 'Staging'].includes(__ENV.PERF_ENVIRONMENT))
    throw new Error('Safe PERF_BASE_URL and PERF_ENVIRONMENT are required.');
  if (!/^\+[0-9]{5,9}$/.test(phonePrefix) || !/^[0-9]{4,8}$/.test(phoneSuffix))
    throw new Error('A dedicated E.164 prefix and numeric race suffix are required.');
  if (!/^\d{6}$/.test(fixedOtp) || password.length < 12)
    throw new Error('Performance OTP/password test configuration is invalid.');

  const phone = `${phonePrefix}${phoneSuffix}`;
  const marker = `PERF-RACE-${__ENV.PERF_RUN_ID}-${raceKind}-${concurrency}`;
  const raceAt = Date.now() + 3000;

  if (raceKind === 'refresh') {
    const challengeId = sendChallenge(phone);
    const register = jsonPost(
      '/api/auth/phone/registration/verify',
      registrationPayload(challengeId, marker));
    const refreshToken = register.status === 200 ? register.json('refreshToken') : null;
    if (!refreshToken) {
      throw new Error(
        `Could not create a refresh-token race fixture. ` +
        `status=${register.status}, body=${register.body.slice(0, 500)}`);
    }
    return { raceAt, marker, refreshToken };
  }

  if (raceKind === 'otp' || raceKind === 'otp-invalid') {
    return { raceAt, marker, challengeIds: [sendChallenge(phone)] };
  }

  if (raceKind === 'registration') {
    return { raceAt, marker, challengeIds: [sendChallenge(phone), sendChallenge(phone)] };
  }

  throw new Error(`Unsupported PERF_RACE_KIND: ${raceKind}`);
}

export default function (data) {
  const waitSeconds = Math.max(0, (data.raceAt - Date.now()) / 1000);
  if (waitSeconds > 0) sleep(waitSeconds);

  let response;
  if (raceKind === 'refresh') {
    response = jsonPost('/api/auth/refresh', { refreshToken: data.refreshToken });
  } else {
    const challengeId = data.challengeIds[(__VU - 1) % data.challengeIds.length];
    const payload = registrationPayload(challengeId, data.marker);
    if (raceKind === 'otp-invalid') payload.code = fixedOtp === '000000' ? '999999' : '000000';
    response = jsonPost(
      '/api/auth/phone/registration/verify',
      payload);
  }

  raceRequests.add(1);
  raceDuration.add(response.timings.duration);
  recordInstance(response);
  serverErrors.add(response.status >= 500);

  const succeeded = response.status === 200;
  const rejected = response.status === 400 || response.status === 401 ||
    response.status === 409 || response.status === 429;
  if (succeeded) raceSuccesses.add(1);
  if (rejected) raceRejections.add(1);
  if (!succeeded && !rejected) invariantFailures.add(1);
  check(response, {
    'race response is deterministic and not a server error': () => succeeded || rejected
  });
}

export function handleSummary(data) {
  const metadata = {
    startedAtUtc,
    raceKind,
    concurrency,
    successCount: metricValue(data, 'race_successes', 'count'),
    rejectionCount: metricValue(data, 'race_rejections', 'count'),
    invariantFailureCount: metricValue(data, 'security_invariant_failures', 'count')
  };
  return performanceArtifacts(data, `auth-${raceKind}-${concurrency}`, metadata);
}
