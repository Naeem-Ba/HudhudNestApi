using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Security.RateLimiting;
using HudhudNestApi.Security.Staging;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Directly unit-tests RedisRateLimitingMiddleware's Staging-smoke-secret exemption check
/// (see its doc comment) without needing a real Redis connection or any ambient
/// RedisRateLimiting__Enabled environment configuration. That matters because production-gate.yml's
/// "Build, Test and Container Gate" job runs with Redis rate limiting off by default (unlike
/// ci.yml's "Build + Test + PostGIS Migrations" job, which forces RedisRateLimiting__Enabled=true
/// for the whole process) -- an end-to-end HTTP test alone would only exercise this branch under
/// one of the two CI workflows. Constructing the middleware directly and asserting whether it
/// calls `next` (exempt, never touches Redis) or falls through toward the Redis-backed check
/// (not exempt) covers both outcomes deterministically, in every CI job, without any Redis
/// dependency at all.
/// </summary>
public sealed class RedisRateLimitingMiddlewareTests
{
    private const string ValidSecret = "unit-test-staging-smoke-secret-at-least-32-characters";

    [Fact(DisplayName = "A valid Staging smoke secret bypasses the Redis-backed limiter entirely")]
    public async Task InvokeAsync_WithValidStagingSmokeSecret_CallsNextWithoutTouchingRedis()
    {
        var nextCalled = false;
        Task Next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        var middleware = CreateMiddleware(Next);
        var httpContext = CreateHttpContext(environmentName: "Staging", secretHeaderValue: ValidSecret);

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled, "The exempt request should have reached the rest of the pipeline.");
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
    }

    [Fact(DisplayName = "No secret proceeds past the exemption check toward the Redis-backed limiter")]
    public async Task InvokeAsync_WithoutSecret_ProceedsPastTheExemptionCheck()
    {
        var nextCalled = false;
        Task Next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        // No IConnectionMultiplexer is registered below, so a non-exempt request reaching the
        // Redis-lookup code fails closed with 503 rather than calling next -- the point here is
        // only that it *tried* (proving the exemption check's false branch executed and did not
        // short-circuit), not exercising a real Redis round trip.
        var middleware = CreateMiddleware(Next);
        var httpContext = CreateHttpContext(environmentName: "Staging", secretHeaderValue: null);

        await middleware.InvokeAsync(httpContext);

        Assert.False(nextCalled, "A non-exempt request must not skip the Redis-backed limiter.");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
    }

    [Fact(DisplayName = "An incorrect secret does not exempt the caller")]
    public async Task InvokeAsync_WithWrongSecret_ProceedsPastTheExemptionCheck()
    {
        var nextCalled = false;
        Task Next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        var middleware = CreateMiddleware(Next);
        var httpContext = CreateHttpContext(environmentName: "Staging", secretHeaderValue: "not-the-real-secret");

        await middleware.InvokeAsync(httpContext);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
    }

    private static RedisRateLimitingMiddleware CreateMiddleware(RequestDelegate next)
    {
        var options = new RedisRateLimitingOptions
        {
            Enabled = true,
            InstanceName = "UnitTest:",
            Policies = new Dictionary<string, RedisFixedWindowRateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["verify-otp"] = new() { PermitLimit = 5, WindowSeconds = 900, QueueLimit = 0 }
            }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:TestSupport:Enabled"] = "true",
                ["Staging:TestSupport:CleanupSecret"] = ValidSecret
            })
            .Build();

        // No IConnectionMultiplexer registered: a non-exempt request that reaches that
        // resolution step fails closed with 503, which is exactly what these tests assert on.
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        return new RedisRateLimitingMiddleware(
            next,
            serviceProvider,
            Microsoft.Extensions.Options.Options.Create(options),
            configuration,
            new TestHostEnvironment("Staging"),
            NullLogger<RedisRateLimitingMiddleware>.Instance);
    }

    private static HttpContext CreateHttpContext(string environmentName, string? secretHeaderValue)
    {
        var httpContext = new DefaultHttpContext();

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(new EnableRateLimitingAttribute("verify-otp")),
            displayName: "test-endpoint");
        httpContext.SetEndpoint(endpoint);

        if (secretHeaderValue is not null)
        {
            httpContext.Request.Headers[StagingTestSupportAuthorization.SecretHeaderName] = secretHeaderValue;
        }

        return httpContext;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = nameof(RedisRateLimitingMiddlewareTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
