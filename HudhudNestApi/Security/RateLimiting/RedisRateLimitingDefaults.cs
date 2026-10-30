namespace HudhudNestApi.Security.RateLimiting;

internal static class RedisRateLimitingDefaults
{
    public static IReadOnlyDictionary<string, RedisFixedWindowRateLimitPolicyOptions> Policies { get; }
        = new Dictionary<string, RedisFixedWindowRateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["send-otp"] = Policy(3, TimeSpan.FromMinutes(15)),
            ["verify-otp"] = Policy(5, TimeSpan.FromMinutes(15)),
            ["auth-password-reset"] = Policy(3, TimeSpan.FromHours(1)),
            // Own buckets so confirming/resending a confirmation link no longer drains the
            // forgot-password budget (see RateLimitingRegistration.cs).
            ["email-verify"] = Policy(20, TimeSpan.FromHours(1)),
            ["email-confirmation-resend"] = Policy(5, TimeSpan.FromHours(1)),
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
            ["public-search"] = PublicReadPolicy(120, TimeSpan.FromMinutes(1)),
            ["geo-search"] = PublicReadPolicy(60, TimeSpan.FromMinutes(1)),
            // RELEASE-BLOCKERS-AR.md B-7: GET /api/agencies/{slug} was anonymous with no
            // limit at all. Same cadence as public-search — it is the same kind of
            // anonymous, browsable public page.
            ["agencies-public"] = PublicReadPolicy(120, TimeSpan.FromMinutes(1)),
            // RELEASE-BLOCKERS-AR.md B-7: shared policy for the remaining anonymous
            // read-only/reference endpoints that had no rate limit at all. Same cadence as
            // public-search/agencies-public.
            ["public-read"] = PublicReadPolicy(120, TimeSpan.FromMinutes(1)),
            // Automatic listing translation: one call per opened listing, and a miss can spend provider
            // quota, so tighter than a read. A stored translation costs one query, a miss one provider call.
            ["listing-translation"] = Policy(30, TimeSpan.FromMinutes(1)),
            ["visits"] = Policy(10, TimeSpan.FromHours(1)),
            ["reviews"] = Policy(5, TimeSpan.FromHours(24)),
            ["service-requests"] = Policy(10, TimeSpan.FromHours(1)),
            ["service-request-documents"] = Policy(20, TimeSpan.FromHours(1)),
            // Messages inside a service request: a negotiation, not a chat room.
            ["service-messages"] = Policy(120, TimeSpan.FromHours(1)),
            // Listing reports — see RateLimitingRegistration's in-memory fallback policy.
            ["listing-reports"] = Policy(10, TimeSpan.FromHours(1)),
            // Joining the marketplace as a provider — same cadence as "contact", which it replaces.
            ["provider-applications"] = Policy(10, TimeSpan.FromHours(1)),
            // Short-Stay Accommodation — same cadence as visits/public-search respectively.
            ["shortstay-booking"] = Policy(10, TimeSpan.FromHours(1)),
            ["shortstay-search"] = PublicReadPolicy(120, TimeSpan.FromMinutes(1)),
            // Phase 5 (Account Deletion): same cadence as auth-password-reset — see
            // RateLimitingRegistration's in-memory fallback policy for the full rationale.
            ["account-delete"] = Policy(3, TimeSpan.FromHours(1)),
            // Finding F7: self-service data export — see RateLimitingRegistration's in-memory
            // fallback policy for the full rationale.
            ["data-export"] = Policy(1, TimeSpan.FromHours(24)),
            // Valuation Stage 7 — see RateLimitingRegistration's in-memory fallback policy for
            // the full rationale (same cadence as visits/service-requests).
            ["valuation-inquiries"] = Policy(10, TimeSpan.FromHours(1)),
            // Security audit 2026-10-03 (F-06, F-02) — see RateLimitingRegistration's in-memory
            // fallback policies for the full rationale.
            ["user-write"] = Policy(60, TimeSpan.FromMinutes(1)),
            ["auth-password-change"] = Policy(5, TimeSpan.FromHours(1)),
            // Agency invite by e-mail/phone — see RateLimitingRegistration's in-memory fallback.
            ["agency-invite"] = Policy(20, TimeSpan.FromHours(1)),
            // Social Sharing / UTM attribution / Social Distribution — see
            // RateLimitingRegistration's in-memory fallback policies for the full rationale.
            // Without an entry here the Redis limiter answered these endpoints with 500
            // ("Rate limit policy is not configured") whenever Redis rate limiting was on.
            ["property-share-events"] = Policy(20, TimeSpan.FromMinutes(1)),
            ["property-attribution-events"] = Policy(40, TimeSpan.FromMinutes(1)),
            ["listing-views"] = Policy(30, TimeSpan.FromMinutes(1)),
            ["social-distribution-write"] = Policy(30, TimeSpan.FromMinutes(1))
        };

    /// <summary>
    /// Anonymous public read policies are counted in process memory (launch audit A6-002): on
    /// Redis every public GET cost at least one command, which exhausts the Upstash free quota
    /// and turns every public page into a 503 ("Rate limiter backend is unavailable").
    /// Login, OTP, reset and every write policy stay on Redis.
    /// </summary>
    private static RedisFixedWindowRateLimitPolicyOptions PublicReadPolicy(int permitLimit, TimeSpan window)
    {
        var policy = Policy(permitLimit, window);
        policy.InProcess = true;
        return policy;
    }

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
