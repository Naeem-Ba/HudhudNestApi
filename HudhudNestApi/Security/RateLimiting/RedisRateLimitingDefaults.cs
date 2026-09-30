namespace HudhudNestApi.Security.RateLimiting;

internal static class RedisRateLimitingDefaults
{
    public static IReadOnlyDictionary<string, RedisFixedWindowRateLimitPolicyOptions> Policies { get; }
        = new Dictionary<string, RedisFixedWindowRateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["send-otp"] = Policy(3, TimeSpan.FromMinutes(15)),
            ["verify-otp"] = Policy(5, TimeSpan.FromMinutes(15)),
            ["auth-password-reset"] = Policy(3, TimeSpan.FromHours(1)),
            ["contact"] = Policy(5, TimeSpan.FromHours(1)),
            // Landing-page waitlist / lead capture — same cadence as "contact".
            ["leads-submit"] = Policy(5, TimeSpan.FromHours(1)),
            // Landing-page willingness-to-pay survey — same cadence as "leads-submit".
            ["surveys-submit"] = Policy(5, TimeSpan.FromHours(1)),
            // Marketing conversion-funnel events — higher cadence, one visit fires several.
            ["marketing-events"] = Policy(60, TimeSpan.FromMinutes(1)),
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
            // RELEASE-BLOCKERS-AR.md B-7: shared policy for the remaining anonymous
            // read-only/reference endpoints that had no rate limit at all. Same cadence as
            // public-search/agencies-public.
            ["public-read"] = Policy(120, TimeSpan.FromMinutes(1)),
            ["visits"] = Policy(10, TimeSpan.FromHours(1)),
            ["reviews"] = Policy(5, TimeSpan.FromHours(24)),
            ["service-requests"] = Policy(10, TimeSpan.FromHours(1)),
            ["service-request-documents"] = Policy(20, TimeSpan.FromHours(1)),
            // Short-Stay Accommodation — same cadence as visits/public-search respectively.
            ["shortstay-booking"] = Policy(10, TimeSpan.FromHours(1)),
            ["shortstay-search"] = Policy(120, TimeSpan.FromMinutes(1)),
            // Phase 5 (Account Deletion): same cadence as auth-password-reset — see
            // RateLimitingRegistration's in-memory fallback policy for the full rationale.
            ["account-delete"] = Policy(3, TimeSpan.FromHours(1)),
            // Finding F7: self-service data export — see RateLimitingRegistration's in-memory
            // fallback policy for the full rationale.
            ["data-export"] = Policy(5, TimeSpan.FromHours(24)),
            // Valuation Stage 7 — see RateLimitingRegistration's in-memory fallback policy for
            // the full rationale (same cadence as visits/service-requests).
            ["valuation-inquiries"] = Policy(10, TimeSpan.FromHours(1))
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
