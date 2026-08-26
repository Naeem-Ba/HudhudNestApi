namespace PropertyApi.Security.RateLimiting;

internal static class RedisRateLimitingDefaults
{
    public static IReadOnlyDictionary<string, RedisFixedWindowRateLimitPolicyOptions> Policies { get; }
        = new Dictionary<string, RedisFixedWindowRateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["send-otp"] = Policy(3, TimeSpan.FromMinutes(15)),
            ["verify-otp"] = Policy(5, TimeSpan.FromMinutes(15)),
            ["auth-password-reset"] = Policy(3, TimeSpan.FromHours(1)),
            ["contact"] = Policy(5, TimeSpan.FromHours(1)),
            ["auth-login"] = Policy(10, TimeSpan.FromMinutes(1)),
            ["auth-register"] = Policy(5, TimeSpan.FromMinutes(10)),
            ["auth-refresh"] = Policy(20, TimeSpan.FromMinutes(5)),
            ["auth-logout"] = Policy(20, TimeSpan.FromMinutes(5)),
            ["public-search"] = Policy(120, TimeSpan.FromMinutes(1)),
            ["geo-search"] = Policy(60, TimeSpan.FromMinutes(1)),
            // RELEASE-BLOCKERS-AR.md B-7: GET /api/agencies/{slug} was anonymous with no
            // limit at all. Same cadence as public-search — it is the same kind of
            // anonymous, browsable public page.
            ["agencies-public"] = Policy(120, TimeSpan.FromMinutes(1)),
            ["visits"] = Policy(10, TimeSpan.FromHours(1)),
            ["reviews"] = Policy(5, TimeSpan.FromHours(24))
        };

    private static RedisFixedWindowRateLimitPolicyOptions Policy(
        int permitLimit,
        TimeSpan window,
        int queueLimit = 0)
        => new()
        {
            PermitLimit = permitLimit,
            WindowSeconds = checked((int)window.TotalSeconds),
            QueueLimit = queueLimit
        };
}
