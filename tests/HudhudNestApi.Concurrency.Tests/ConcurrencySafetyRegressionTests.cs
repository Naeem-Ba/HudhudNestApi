using Xunit;

namespace HudhudNestApi.Concurrency.Tests;

public sealed class ConcurrencySafetyRegressionTests
{
    [Fact]
    public void Refresh_and_otp_races_use_coordinated_concurrent_executors()
    {
        var script = File.ReadAllText(Repo("performance", "load-tests", "scenarios", "auth-races.js"));
        Assert.Contains("per-vu-iterations", script, StringComparison.Ordinal);
        Assert.Contains("raceAt", script, StringComparison.Ordinal);
        Assert.Contains("race_successes", script, StringComparison.Ordinal);
        Assert.Contains("security_invariant_failures", script, StringComparison.Ordinal);
        Assert.Contains("count==1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_otp_attempt_increment_is_atomically_bounded()
    {
        var workflow = File.ReadAllText(Repo(
            "HudhudNestApi.Infrastructure", "Auth", "Services", "PhoneAuthenticationWorkflow.cs"));
        Assert.Contains("x.Id == id && x.AttemptCount < 3", workflow, StringComparison.Ordinal);
        Assert.Contains("x.AttemptCount + 1", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Distributed_rate_limit_test_requires_exact_limit_and_window_recovery()
    {
        var script = File.ReadAllText(Repo(
            "performance", "load-tests", "scenarios", "rate-limit-race.js"));
        Assert.Contains("rate_limit_allowed", script, StringComparison.Ordinal);
        Assert.Contains("count==${configuredLimit}", script, StringComparison.Ordinal);
        Assert.Contains("rate_limit_recovered", script, StringComparison.Ordinal);
        Assert.Contains("instance_api_1", script, StringComparison.Ordinal);
        Assert.Contains("instance_api_2", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Authoritative_database_integrity_is_mandatory_for_auth_races()
    {
        var runner = File.ReadAllText(Repo("scripts", "run-performance-tests.sh"));
        var sql = File.ReadAllText(Repo("performance", "sql", "capture-auth-integrity.sql"));
        Assert.Contains("capture-auth-race-integrity.sh", runner, StringComparison.Ordinal);
        Assert.Contains("active_refresh_token_count", sql, StringComparison.Ordinal);
        Assert.Contains("consumed_otp_count", sql, StringComparison.Ordinal);
        Assert.Contains("maximum_otp_attempt_count", sql, StringComparison.Ordinal);
        Assert.Contains("invariant_passed", sql, StringComparison.Ordinal);
    }

    private static string Repo(params string[] components)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(components).ToArray());
    }
}
