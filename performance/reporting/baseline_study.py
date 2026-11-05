#!/usr/bin/env python3
"""Statistics for rebuilding and judging HTTP performance baselines.

This module is ADVISORY tooling. It does not read or write
performance/baselines/approved-baseline.json, does not change any threshold in
performance/performance-budgets.json and is not called by the Production Gate. It exists to answer
three questions with data instead of a single number:

  1. summarize        -- what did ONE complete run measure (steady state only, warm-up excluded)?
  2. build-candidate  -- what do N independent runs say, how much do they disagree, and is that
                         enough evidence to propose a baseline (status: pending-approval)?
  3. compare          -- is a new set of runs different from the baseline beyond run-to-run noise,
                         and if the noise is too high to tell, say so instead of guessing.
  4. sensitivity      -- how often would a given rule flag a synthetic regression of d% (and how
                         often would it flag nothing at all)?

Inference is done on RUN-LEVEL percentiles (one p95 per independent run, on a log scale), never on
pooled requests: requests inside a run share one runner, one warm JIT and one cache and are
strongly autocorrelated, so treating 10 000 of them as independent samples would manufacture
certainty that does not exist.

Standard library only.
"""
import argparse
import functools
import gzip
import hashlib
import json
import math
import pathlib
import random
import statistics
import sys
from datetime import datetime, timezone

# ----------------------------------------------------------------------------------------------
# primitives
# ----------------------------------------------------------------------------------------------


def percentile(sorted_values, p):
    """Linear-interpolation percentile (numpy default). p in [0, 100]; input must be sorted."""
    if not sorted_values:
        raise ValueError("percentile of an empty sample")
    if len(sorted_values) == 1:
        return float(sorted_values[0])
    rank = (p / 100.0) * (len(sorted_values) - 1)
    lo = int(math.floor(rank))
    hi = min(lo + 1, len(sorted_values) - 1)
    return float(sorted_values[lo] + (sorted_values[hi] - sorted_values[lo]) * (rank - lo))


def _betacf(a, b, x):
    tiny = 1e-30
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    d = 1.0 / (d if abs(d) > tiny else tiny)
    h = d
    for m in range(1, 300):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < 3e-14:
            break
    return h


def _betai(a, b, x):
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    bt = math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log1p(-x))
    if x < (a + 1.0) / (a + b + 2.0):
        return bt * _betacf(a, b, x) / a
    return 1.0 - bt * _betacf(b, a, 1.0 - x) / b


def t_cdf(t, df):
    x = df / (df + t * t)
    tail = 0.5 * _betai(df / 2.0, 0.5, x)
    return 1.0 - tail if t > 0 else tail


@functools.lru_cache(maxsize=4096)
def t_quantile(prob, df):
    """Inverse Student-t CDF by bisection (prob in (0,1))."""
    lo, hi = -200.0, 200.0
    for _ in range(70):
        mid = (lo + hi) / 2.0
        if t_cdf(mid, df) < prob:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2.0


def robust_spread(values):
    values = list(values)
    med = statistics.median(values)
    mad = statistics.median(abs(v - med) for v in values) * 1.4826
    mean = statistics.fmean(values)
    sd = statistics.stdev(values) if len(values) > 1 else 0.0
    return {
        "n": len(values), "median": med, "mean": mean, "min": min(values), "max": max(values),
        "sd": sd, "cv": (sd / mean) if mean else 0.0, "madSigma": mad,
    }


def bootstrap_ci(values, statistic=statistics.median, iterations=4000, level=0.95, seed=20260804):
    rng = random.Random(seed)
    n = len(values)
    stats = sorted(statistic([values[rng.randrange(n)] for _ in range(n)]) for _ in range(iterations))
    a = (1 - level) / 2
    return stats[int(a * iterations)], stats[min(iterations - 1, int((1 - a) * iterations))]


# ----------------------------------------------------------------------------------------------
# summarize (one run)
# ----------------------------------------------------------------------------------------------


def _open(path):
    path = pathlib.Path(path)
    return gzip.open(path, "rt", encoding="utf-8") if path.suffix == ".gz" else open(path, encoding="utf-8")


def _parse_time(text):
    text = text.replace("Z", "+00:00")
    if "." in text:  # k6 emits nanoseconds; datetime accepts at most microseconds
        head, rest = text.split(".", 1)
        frac = ""
        i = 0
        while i < len(rest) and rest[i].isdigit():
            frac += rest[i]
            i += 1
        text = f"{head}.{frac[:6].ljust(6, '0')}{rest[i:]}"
    return datetime.fromisoformat(text).timestamp()


def read_k6_points(path, scenario="steady_state", metric="http_req_duration"):
    """Return [(epoch_seconds, value_ms, endpoint_tag)] for one k6 --out json file."""
    points = []
    with _open(path) as handle:
        for line in handle:
            if '"type":"Point"' not in line or f'"metric":"{metric}"' not in line:
                continue
            row = json.loads(line)
            data = row["data"]
            tags = data.get("tags") or {}
            if scenario and tags.get("scenario") != scenario:
                continue
            points.append((_parse_time(data["time"]), float(data["value"]), tags.get("endpoint", "")))
    points.sort()
    return points


def summarize_points(points, label, window_seconds=10.0):
    if not points:
        raise ValueError(f"{label}: no steady-state http_req_duration points")
    values = sorted(p[1] for p in points)
    start, end = points[0][0], points[-1][0]
    duration = max(end - start, 1e-9)
    by_endpoint = {}
    for _, value, endpoint in points:
        by_endpoint.setdefault(endpoint or "untagged", []).append(value)
    windows = {}
    for t, value, _ in points:
        windows.setdefault(int((t - start) // window_seconds), []).append(value)
    window_p95 = [percentile(sorted(v), 95) for _, v in sorted(windows.items()) if len(v) >= 20]
    return {
        "label": label,
        "requests": len(values),
        "durationSeconds": duration,
        "requestsPerSecond": len(values) / duration,
        "p50": percentile(values, 50), "p95": percentile(values, 95), "p99": percentile(values, 99),
        "max": values[-1],
        "byEndpointP95": {k: percentile(sorted(v), 95) for k, v in sorted(by_endpoint.items())},
        "byEndpointRequests": {k: len(v) for k, v in sorted(by_endpoint.items())},
        "windowP95": window_p95,
        "windowSeconds": window_seconds,
    }


# ----------------------------------------------------------------------------------------------
# candidate baseline
# ----------------------------------------------------------------------------------------------

METRICS = ("p50", "p95", "p99")


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def required_runs(cv, relative_half_width=0.05, confidence=0.95):
    """Runs needed so the mean's CI half-width is <= relative_half_width of the mean."""
    if cv <= 0:
        return 3
    for n in range(2, 200):
        t = t_quantile(1 - (1 - confidence) / 2, n - 1)
        if t * cv / math.sqrt(n) <= relative_half_width:
            return max(n, 3)
    return 200


def build_candidate(summaries, scenario, meta, min_runs=3, min_requests=1000, max_cv=0.30):
    reasons = []
    if len(summaries) < min_runs:
        reasons.append(f"only {len(summaries)} run(s); at least {min_runs} independent runs are required")
    small = [s["label"] for s in summaries if s["requests"] < min_requests]
    if small:
        reasons.append(f"runs with fewer than {min_requests} steady-state requests: {', '.join(small)}")
    result = {"scenario": scenario, "runs": len(summaries), "perRun": []}
    for s in summaries:
        result["perRun"].append({k: s[k] for k in ("label", "requests", "requestsPerSecond", "p50", "p95", "p99", "max")})
    for metric in METRICS + ("requestsPerSecond",):
        values = [s[metric] for s in summaries]
        spread = robust_spread(values)
        if len(values) >= 3:
            lo, hi = bootstrap_ci(values)
            spread["median95CI"] = [lo, hi]
        result[metric] = spread
    cv = result["p95"]["cv"]
    needed = required_runs(cv)
    if cv > max_cv:
        reasons.append(f"p95 run-to-run CV {cv:.1%} exceeds {max_cv:.0%}; runs disagree too much for one baseline value")
    result["runsNeededForFivePercentHalfWidth"] = needed
    if len(summaries) < needed:
        reasons.append(f"{needed} runs would be needed for a +/-5% confidence half-width on p95; {len(summaries)} collected")
    drift = []
    for s in summaries:
        w = s.get("windowP95") or []
        if len(w) >= 4:
            third = max(1, len(w) // 3)
            drift.append(statistics.median(w[-third:]) / statistics.median(w[:third]) - 1)
    result["steadyStateDriftFirstVsLastThird"] = drift
    result["valid"] = not reasons
    result["invalidReasons"] = reasons
    result["representativeRun"] = min(result["perRun"], key=lambda r: abs(r["p95"] - result["p95"]["median"]))["label"]
    return {
        "schemaVersion": 2,
        "kind": "candidate-baseline",
        "approval": {
            "status": "pending-approval",
            "note": "Candidate only. Not read by the Production Gate. Becomes authoritative only through the "
                    "update procedure in docs/performance/performance-baseline-policy.md.",
        },
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "meta": meta,
        "scenario": result,
    }


# ----------------------------------------------------------------------------------------------
# advisory comparison
# ----------------------------------------------------------------------------------------------


def compare_runs(baseline_p95, current_p95, alpha=0.05, power=0.80, min_effect=0.03, noisy_mde=0.10):
    """One-sided (regression) two-sample t on log run-level p95, pooled variance.

    With a single current run the variance comes from the baseline runs only (prediction-interval
    semantics). Returns a verdict plus the smallest regression this data could have detected.
    """
    if len(baseline_p95) < 3:
        return {"verdict": "INCONCLUSIVE", "reason": "fewer than 3 baseline runs"}
    if not current_p95:
        return {"verdict": "INCONCLUSIVE", "reason": "no current runs"}
    lb = [math.log(v) for v in baseline_p95]
    lc = [math.log(v) for v in current_p95]
    nb, nc = len(lb), len(lc)
    vb = statistics.variance(lb)
    vc = statistics.variance(lc) if nc > 1 else None
    if vc is None:
        pooled, df = vb, nb - 1
    else:
        pooled = ((nb - 1) * vb + (nc - 1) * vc) / (nb + nc - 2)
        df = nb + nc - 2
    se = math.sqrt(pooled * (1 / nb + 1 / nc))
    diff = statistics.fmean(lc) - statistics.fmean(lb)
    t = diff / se if se > 0 else (math.inf if diff > 0 else (-math.inf if diff < 0 else 0.0))
    p_up = 1 - t_cdf(t, df) if math.isfinite(t) else (0.0 if t > 0 else 1.0)
    p_down = t_cdf(t, df) if math.isfinite(t) else (1.0 if t > 0 else 0.0)
    mde_log = (t_quantile(1 - alpha, df) + t_quantile(power, df)) * se
    mde = math.exp(mde_log) - 1
    rel = math.exp(diff) - 1
    out = {
        "baselineRuns": nb, "currentRuns": nc, "baselineMedianP95": statistics.median(baseline_p95),
        "currentMedianP95": statistics.median(current_p95), "relativeChangeOfGeoMean": rel,
        "pValueRegression": p_up, "pValueImprovement": p_down, "df": df,
        "minimumDetectableRegression": mde, "alpha": alpha, "power": power, "minEffect": min_effect,
    }
    if p_up < alpha and rel >= min_effect:
        out["verdict"] = "REGRESSION"
    elif p_down < alpha and rel <= -min_effect:
        out["verdict"] = "IMPROVEMENT"
    elif mde > noisy_mde:
        out["verdict"] = "INCONCLUSIVE"
        out["reason"] = (f"noise is too high to see a change smaller than {mde:.0%} with {nb} baseline and "
                         f"{nc} current run(s); collect more runs instead of trusting a point comparison")
    else:
        out["verdict"] = "NO_SIGNIFICANT_CHANGE"
    return out


def legacy_rule(baseline_value, current_value, p95_percent=15.0):
    """The current Production Gate rule (performance/reporting/compare.py): one current p95 against
    one approved value; fails when the increase exceeds p95Percent."""
    return (current_value - baseline_value) / baseline_value * 100.0 > p95_percent


def simulate(pool, deltas, n_base, n_cur, reps, seed=7, alpha=0.05, min_effect=0.03):
    """Monte-Carlo detection rates. `pool` = real run-level p95 values (independent runs). A synthetic
    regression d multiplies every current run's p95 by (1 + d): a latency percentile scales linearly
    with a uniform slowdown, so this is exact for that regression model."""
    rng = random.Random(seed)
    rows = []
    for d in deltas:
        legacy = advisory = inconclusive = 0
        for _ in range(reps):
            base = [pool[rng.randrange(len(pool))] for _ in range(n_base)]
            cur = [pool[rng.randrange(len(pool))] * (1 + d) for _ in range(n_cur)]
            if legacy_rule(statistics.median(base), statistics.median(cur)):
                legacy += 1
            verdict = compare_runs(base, cur, alpha=alpha, min_effect=min_effect)["verdict"]
            if verdict == "REGRESSION":
                advisory += 1
            elif verdict == "INCONCLUSIVE":
                inconclusive += 1
        rows.append({"delta": d, "legacyRuleDetects": legacy / reps, "advisoryDetects": advisory / reps,
                     "advisoryInconclusive": inconclusive / reps})
    return rows


# ----------------------------------------------------------------------------------------------
# CLI
# ----------------------------------------------------------------------------------------------


def _dump(obj, path=None):
    text = json.dumps(obj, indent=2, sort_keys=False)
    if path:
        pathlib.Path(path).write_text(text + "\n", encoding="utf-8")
    else:
        print(text)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    s = sub.add_parser("summarize")
    s.add_argument("raw", help="k6 --out json file (.json or .json.gz)")
    s.add_argument("--label", required=True)
    s.add_argument("--scenario", default="steady_state")
    s.add_argument("--output")

    b = sub.add_parser("build-candidate")
    b.add_argument("summaries", nargs="+")
    b.add_argument("--scenario", required=True)
    b.add_argument("--meta", help="JSON file with commit/runtime/environment provenance")
    b.add_argument("--output")

    c = sub.add_parser("compare")
    c.add_argument("--baseline-p95", nargs="+", type=float, required=True)
    c.add_argument("--current-p95", nargs="+", type=float, required=True)
    c.add_argument("--min-effect", type=float, default=0.03)

    m = sub.add_parser("sensitivity")
    m.add_argument("--pool", nargs="+", type=float, required=True, help="real run-level p95 values")
    m.add_argument("--base-runs", type=int, default=3)
    m.add_argument("--current-runs", type=int, default=3)
    m.add_argument("--reps", type=int, default=4000)
    m.add_argument("--deltas", nargs="+", type=float, default=[0.0, 0.03, 0.05, 0.08, 0.10, 0.15, 0.25])

    args = parser.parse_args(argv)
    if args.command == "summarize":
        _dump(summarize_points(read_k6_points(args.raw, args.scenario), args.label), args.output)
    elif args.command == "build-candidate":
        summaries = [json.loads(pathlib.Path(p).read_text(encoding="utf-8")) for p in args.summaries]
        meta = json.loads(pathlib.Path(args.meta).read_text(encoding="utf-8")) if args.meta else {}
        _dump(build_candidate(summaries, args.scenario, meta), args.output)
    elif args.command == "compare":
        _dump(compare_runs(args.baseline_p95, args.current_p95, min_effect=args.min_effect))
    elif args.command == "sensitivity":
        _dump(simulate(args.pool, args.deltas, args.base_runs, args.current_runs, args.reps))
    return 0


if __name__ == "__main__":
    sys.exit(main())
