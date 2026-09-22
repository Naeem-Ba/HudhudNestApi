using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Security.Staging;

namespace PropertyApi.Configuration;

public static class RateLimitingRegistration
{
    public static IServiceCollection AddPropertyApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("send-otp", httpContext =>
                BuildOtpPolicyPartition(httpContext, permitLimit: 3, window: TimeSpan.FromMinutes(15)));

            // verify-otp's shared PermitLimit is a real, deliberate anti-brute-force control
            // for every ordinary caller. On Staging, ForwardedHeaders__Enabled=false collapses
            // every caller behind Render's single edge IP (see docs/testing/staging-smoke-
            // runbook.md / render-staging-deployment memory), so the mandatory smoke suite's
            // own structurally-required call pattern (wrong-code, correct-code, one-time-use
            // recheck, per user) shares this bucket with every other Staging visitor and would
            // exhaust it on its own. Exempting only requests carrying the Staging smoke
            // automation's own shared secret (the same one build-info/cleanup already require;
            // see StagingTestSupportAuthorization) keeps the real limit intact for every actual
            // visitor -- in Staging and in Production, where this check is always false.
            options.AddPolicy("verify-otp", httpContext =>
                BuildOtpPolicyPartition(httpContext, permitLimit: 5, window: TimeSpan.FromMinutes(15)));

            options.AddPolicy("auth-password-reset", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("contact", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Landing-page waitlist / lead capture — same cadence as "contact", the closest
            // existing equivalent (one anonymous, unauthenticated form submission).
            options.AddPolicy("leads-submit", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Landing-page willingness-to-pay survey — same cadence as "leads-submit": one
            // anonymous, unauthenticated submission per visitor.
            options.AddPolicy("surveys-submit", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Marketing conversion-funnel events (page view, CTA click, ...) — much higher
            // cadence than a form submission since a single visit fires several of these.
            options.AddPolicy("marketing-events", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-register", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-refresh", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("auth-logout", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("public-search", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Social Sharing & Distribution — POST /properties/{id}/share-events. Anonymous
            // (most sharers aren't logged in) and only ever fires on an already-successful
            // share/copy-link, so real traffic per visitor is low; the limit exists purely to
            // bound abuse, not to constrain a legitimate user bouncing between platforms.
            options.AddPolicy("property-share-events", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // UTM / Attribution (Phase 2) — POST /properties/{id}/attribution-events. Anonymous,
            // and fires more often per visitor than a share event (one per page view, plus one
            // per contact/lead action), so it gets a higher cadence than "property-share-events"
            // — same order of magnitude as "marketing-events", the closest existing equivalent
            // (an anonymous, high-frequency, page-driven funnel-tracking endpoint).
            options.AddPolicy("property-attribution-events", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 40,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Social Distribution (Phase 3) — every write endpoint under
            // /api/social-distribution/*. Authenticated Admin-only traffic (unlike the two
            // anonymous policies above), so this exists purely as defense-in-depth against a
            // compromised/scripted admin session hammering the publish/queue/retry endpoints,
            // not against anonymous abuse.
            options.AddPolicy("social-distribution-write", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("geo-search", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // RELEASE-BLOCKERS-AR.md B-7: GET /api/agencies/{slug} was anonymous with no rate
            // limit at all. Same cadence as public-search.
            options.AddPolicy("agencies-public", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // RELEASE-BLOCKERS-AR.md B-7: shared policy for every anonymous, read-only (or
            // functionally read-only) endpoint that had no rate limit at all -- reference/lookup
            // data, public reference profiles, and the CSRF token handshake. Same generous cadence
            // as public-search/agencies-public; see RateLimitingGuardTests for the exact endpoint
            // list this is pinned to.
            options.AddPolicy("public-read", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("visits", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("reviews", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(24),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Short-Stay Accommodation — same cadence as "visits", the closest existing
            // equivalent (one user-initiated request-creation flow against a limited resource).
            options.AddPolicy("shortstay-booking", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Same cadence as "public-search" — anonymous, read-only listing search.
            options.AddPolicy("shortstay-search", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // HudhudNest Services Marketplace — same cadence as "visits", the closest existing
            // equivalent (one user-initiated request-creation flow against a limited resource).
            options.AddPolicy("service-requests", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy("service-request-documents", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Phase 5 (Account Deletion): irreversible, high-risk, authenticated action.
            // Same cadence as auth-password-reset -- the closest existing equivalent (a rare,
            // sensitive, per-identity operation that must not be brute-forceable).
            options.AddPolicy("account-delete", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): self-service data
            // export. A heavier read than most /me endpoints (assembles the account's full
            // owned-data graph), so capped lower than an ordinary authenticated GET but not as
            // tightly as account-delete -- there is no irreversible action here to brute-force.
            options.AddPolicy("data-export", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(24),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Valuation Stage 7: submitting a valuation request is anonymous by design
            // (ValuationInquiry.RequesterId is nullable for a guest visitor) and, unlike a
            // plain read, creates rows and can trigger office-matching queries against
            // agencies -- same cadence as "visits"/"service-requests", the closest existing
            // equivalent (one user-initiated request-creation flow against a limited resource).
            options.AddPolicy("valuation-inquiries", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    // Extracted from the "send-otp"/"verify-otp" AddPolicy lambdas so a test can call it
    // directly. Those lambdas capture nothing but static-method calls, so the C# compiler
    // caches each as a single static delegate instance shared across every AddRateLimiter
    // configure-call -- ASP.NET Core's own rate-limiter middleware also only invokes a
    // policy's factory once per distinct partition key it has not seen before (caching the
    // resulting RateLimiter afterward), not once per request. Coverage tooling attributes
    // hits to a lambda's body only when the compiled delegate is actually invoked, so a policy
    // exercised solely through end-to-end HTTP calls that all land on the same handful of
    // partition keys can under-report -- calling this method directly from a unit test removes
    // that dependency on the rate limiter's own invocation/caching timing entirely.
    internal static RateLimitPartition<string> BuildOtpPolicyPartition(
        HttpContext httpContext, int permitLimit, TimeSpan window)
    {
        if (IsExemptStagingSmokeTraffic(httpContext))
        {
            return RateLimitPartition.GetNoLimiter(GetClientRateLimitPartitionKey(httpContext));
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    }

    private static bool IsExemptStagingSmokeTraffic(HttpContext httpContext)
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var environment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        return StagingTestSupportAuthorization.IsAuthorized(httpContext.Request, configuration, environment);
    }

    private static string GetClientRateLimitPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User?
            .FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        return $"ip:{ip}";
    }
}
