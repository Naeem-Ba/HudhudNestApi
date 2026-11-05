import gzip
import json
import math
import pathlib
import random
import statistics
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import baseline_study as bs  # noqa: E402


def k6_line(time, value, scenario="steady_state", endpoint="property-list", metric="http_req_duration"):
    return json.dumps({"type": "Point", "metric": metric,
                       "data": {"time": time, "value": value, "tags": {"scenario": scenario, "endpoint": endpoint}}},
                      separators=(",", ":"))


class PrimitiveTests(unittest.TestCase):
    def test_percentile_matches_linear_interpolation(self):
        values = list(range(1, 101))
        self.assertAlmostEqual(bs.percentile(values, 50), 50.5)
        self.assertAlmostEqual(bs.percentile(values, 95), 95.05)
        self.assertEqual(bs.percentile([7], 99), 7.0)

    def test_t_distribution_against_known_values(self):
        self.assertAlmostEqual(bs.t_quantile(0.975, 10), 2.2281, places=3)
        self.assertAlmostEqual(bs.t_quantile(0.95, 4), 2.1318, places=3)
        self.assertAlmostEqual(bs.t_cdf(0.0, 5), 0.5, places=9)

    def test_robust_spread_ignores_one_outlier_in_madsigma(self):
        spread = bs.robust_spread([100, 101, 99, 100, 500])
        self.assertLess(spread["madSigma"], 3)
        self.assertGreater(spread["sd"], 100)

    def test_required_runs_grows_with_noise(self):
        self.assertLessEqual(bs.required_runs(0.02), 3)
        self.assertGreater(bs.required_runs(0.25), bs.required_runs(0.10))


class SummarizeTests(unittest.TestCase):
    def test_only_steady_state_points_are_counted_and_gz_is_read(self):
        lines = [k6_line("2026-11-04T10:00:00.000000000Z", 9999, scenario="warmup")]
        for i in range(200):
            lines.append(k6_line(f"2026-11-04T10:00:{i // 10:02d}.{i % 10}00000000Z", 10 + (i % 10)))
        lines.append(k6_line("2026-11-04T10:00:01.0Z", 5, metric="http_req_waiting"))
        with tempfile.TemporaryDirectory() as tmp:
            path = pathlib.Path(tmp) / "raw.json.gz"
            with gzip.open(path, "wt") as handle:
                handle.write("\n".join(lines) + "\n")
            points = bs.read_k6_points(path)
            summary = bs.summarize_points(points, "r1")
        self.assertEqual(summary["requests"], 200)
        self.assertLess(summary["max"], 100)  # the 9999 ms warm-up point is excluded
        self.assertEqual(summary["byEndpointRequests"], {"property-list": 200})

    def test_empty_run_is_rejected(self):
        with self.assertRaises(ValueError):
            bs.summarize_points([], "x")


def fake_summary(label, p95, requests=5000):
    return {"label": label, "requests": requests, "requestsPerSecond": 80.0, "p50": p95 / 2, "p95": p95,
            "p99": p95 * 1.3, "max": p95 * 2, "windowP95": [p95] * 6}


class CandidateTests(unittest.TestCase):
    def test_three_stable_runs_are_valid_but_still_pending_approval(self):
        cand = bs.build_candidate([fake_summary(f"r{i}", v) for i, v in enumerate([100, 103, 98])], "browse-diverse", {})
        self.assertEqual(cand["approval"]["status"], "pending-approval")
        self.assertEqual(cand["kind"], "candidate-baseline")

    def test_too_few_runs_are_invalid(self):
        cand = bs.build_candidate([fake_summary("a", 100), fake_summary("b", 101)], "x", {})
        self.assertFalse(cand["scenario"]["valid"])
        self.assertTrue(any("at least 3" in r for r in cand["scenario"]["invalidReasons"]))

    def test_noisy_runs_are_invalid(self):
        cand = bs.build_candidate([fake_summary(f"r{i}", v) for i, v in enumerate([100, 220, 60, 400])], "x", {})
        self.assertFalse(cand["scenario"]["valid"])

    def test_short_runs_are_invalid(self):
        cand = bs.build_candidate([fake_summary(f"r{i}", 100, requests=100) for i in range(3)], "x", {})
        self.assertFalse(cand["scenario"]["valid"])


class CompareTests(unittest.TestCase):
    base = [100.0, 101.0, 99.0, 100.5, 99.5]

    def test_identical_distribution_is_not_flagged(self):
        self.assertEqual(bs.compare_runs(self.base, [100.2, 99.8, 100.4])["verdict"], "NO_SIGNIFICANT_CHANGE")

    def test_clear_regression_is_flagged(self):
        self.assertEqual(bs.compare_runs(self.base, [110.0, 110.5, 109.5])["verdict"], "REGRESSION")

    def test_clear_improvement_is_reported_not_flagged_as_regression(self):
        self.assertEqual(bs.compare_runs(self.base, [80.0, 80.5, 79.5])["verdict"], "IMPROVEMENT")

    def test_noisy_baseline_yields_inconclusive_instead_of_a_verdict(self):
        noisy = [60.0, 150.0, 90.0, 210.0, 120.0]
        result = bs.compare_runs(noisy, [125.0])
        self.assertEqual(result["verdict"], "INCONCLUSIVE")
        self.assertGreater(result["minimumDetectableRegression"], 0.10)

    def test_too_little_baseline_data_is_inconclusive(self):
        self.assertEqual(bs.compare_runs([100, 101], [100])["verdict"], "INCONCLUSIVE")

    def test_legacy_rule_is_a_single_ratio(self):
        self.assertFalse(bs.legacy_rule(100, 115))
        self.assertTrue(bs.legacy_rule(100, 115.1))
        self.assertFalse(bs.legacy_rule(418.59, 5.0))  # a 100x regression from 5 ms stays invisible


class SimulationTests(unittest.TestCase):
    def test_false_positive_rate_without_regression_is_bounded(self):
        rng = random.Random(3)
        pool = [100 * math.exp(rng.gauss(0, 0.02)) for _ in range(200)]
        rows = bs.simulate(pool, [0.0], n_base=5, n_cur=3, reps=1500)
        self.assertLess(rows[0]["advisoryDetects"], 0.10)

    def test_power_grows_with_effect_size(self):
        rng = random.Random(4)
        pool = [100 * math.exp(rng.gauss(0, 0.03)) for _ in range(200)]
        rows = {r["delta"]: r for r in bs.simulate(pool, [0.0, 0.05, 0.15, 0.25], 5, 3, 1500)}
        self.assertLess(rows[0.05]["advisoryDetects"], rows[0.15]["advisoryDetects"] + 1e-9)
        self.assertGreater(rows[0.15]["advisoryDetects"], 0.95)
        self.assertGreater(rows[0.25]["advisoryDetects"], 0.99)

    def test_noise_makes_small_regressions_undetectable_and_is_reported_as_inconclusive(self):
        rng = random.Random(5)
        pool = [4.0 * math.exp(rng.gauss(0, 0.2)) for _ in range(200)]
        rows = {r["delta"]: r for r in bs.simulate(pool, [0.0, 0.05], 3, 1, 1500)}
        self.assertLess(rows[0.05]["advisoryDetects"], 0.2)
        self.assertGreater(rows[0.05]["advisoryInconclusive"], 0.5)


if __name__ == "__main__":
    unittest.main()
