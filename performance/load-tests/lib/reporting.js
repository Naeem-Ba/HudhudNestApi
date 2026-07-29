export function metricValue(data, name, value, fallback = 0) {
  const metric = data.metrics[name];
  if (!metric || !metric.values || metric.values[value] === undefined) return fallback;
  return metric.values[value];
}

function xmlEscape(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&apos;');
}

export function performanceArtifacts(data, scenario, metadata = {}) {
  const thresholds = [];
  for (const [metricName, metric] of Object.entries(data.metrics)) {
    if (!metric.thresholds) continue;
    for (const [expression, result] of Object.entries(metric.thresholds)) {
      thresholds.push({ metric: metricName, expression, passed: result.ok === true });
    }
  }

  const failedThresholds = thresholds.filter((threshold) => !threshold.passed);
  const summary = {
    scenario,
    runId: __ENV.PERF_RUN_ID,
    environment: __ENV.PERF_ENVIRONMENT,
    startedAtUtc: metadata.startedAtUtc || null,
    completedAtUtc: new Date().toISOString(),
    virtualUsers: Number(metadata.concurrency || metadata.requestCount ||
      metadata.virtualUsers || __ENV.PERF_VIRTUAL_USERS || 0),
    duration: __ENV.PERF_TEST_DURATION || metadata.duration || null,
    requestCount: metricValue(data, 'http_reqs', 'count'),
    requestsPerSecond: metricValue(data, 'http_reqs', 'rate'),
    p50Milliseconds: metricValue(data, 'http_req_duration', 'p(50)'),
    p95Milliseconds: metricValue(data, 'http_req_duration', 'p(95)'),
    p99Milliseconds: metricValue(data, 'http_req_duration', 'p(99)'),
    maximumMilliseconds: metricValue(data, 'http_req_duration', 'max'),
    errorRate: metricValue(data, 'http_req_failed', 'rate'),
    serverErrorRate: metricValue(data, 'server_errors', 'rate'),
    checkFailureRate: 1 - metricValue(data, 'checks', 'rate', 1),
    instances: {
      api1Requests: metricValue(data, 'instance_api_1', 'count'),
      api2Requests: metricValue(data, 'instance_api_2', 'count')
    },
    thresholds,
    thresholdsPassed: failedThresholds.length === 0,
    metadata
  };

  const markdown = [
    `# ${scenario} performance result`,
    '',
    `- Run: \`${summary.runId}\``,
    `- Environment: \`${summary.environment}\``,
    `- Requests: ${summary.requestCount}`,
    `- Requests/second: ${summary.requestsPerSecond.toFixed(2)}`,
    `- p50: ${summary.p50Milliseconds.toFixed(2)} ms`,
    `- p95: ${summary.p95Milliseconds.toFixed(2)} ms`,
    `- p99: ${summary.p99Milliseconds.toFixed(2)} ms`,
    `- Error rate: ${(summary.errorRate * 100).toFixed(3)}%`,
    `- Instances: api-1=${summary.instances.api1Requests}, api-2=${summary.instances.api2Requests}`,
    `- Result: **${summary.thresholdsPassed ? 'Passed' : 'Failed'}**`,
    '',
    '| Metric | Threshold | Result |',
    '|---|---|---:|',
    ...thresholds.map((threshold) =>
      `| ${threshold.metric} | ${threshold.expression} | ${threshold.passed ? 'Passed' : 'Failed'} |`)
  ].join('\n');

  const failures = failedThresholds.map((threshold) =>
    `<failure message="${xmlEscape(`${threshold.metric}: ${threshold.expression}`)}" />`).join('');
  const junit = `<?xml version="1.0" encoding="UTF-8"?>\n` +
    `<testsuite name="${xmlEscape(scenario)}" tests="1" failures="${failedThresholds.length ? 1 : 0}" skipped="0">` +
    `<testcase classname="PropertyApi.Performance" name="${xmlEscape(scenario)}">${failures}</testcase>` +
    `</testsuite>\n`;

  const root = `/artifacts/performance/k6/${scenario}`;
  return {
    [`${root}.json`]: JSON.stringify(summary, null, 2),
    [`${root}.md`]: markdown,
    [`${root}.junit.xml`]: junit
  };
}
