using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Security.RateLimiting;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Launch audit A6-002: the anonymous public read policies are counted in process memory, so a
/// public GET costs no Redis command (Upstash free quota) and keeps working when Redis is out.
/// No IConnectionMultiplexer is registered here: any policy that still reached Redis would answer 503.
/// </summary>
public sealed class InProcessPublicReadRateLimitTests
{
    private static readonly string[] PublicReadPolicies =
        ["public-search", "geo-search", "agencies-public", "public-read", "shortstay-search"];

    [Fact(DisplayName = "Public read policies are counted in process, auth and write policies stay on Redis")]
    public void Defaults_MarkOnlyPublicReadPoliciesInProcess()
    {
        foreach (var name in PublicReadPolicies)
            Assert.True(RedisRateLimitingDefaults.Policies[name].InProcess == true, name);

        foreach (var name in new[] { "auth-login", "send-otp", "verify-otp", "auth-password-reset", "auth-register", "visits", "reviews", "listing-views" })
            Assert.False(RedisRateLimitingDefaults.Policies[name].InProcess == true, name);
    }

    [Fact(DisplayName = "Overriding a public policy's limit in configuration keeps it in process")]
    public void ConfiguredLimitOverride_KeepsInProcessDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Redis:Enabled"] = "true",
                ["RateLimiting:Redis:ConnectionString"] = "redis.invalid:6379,abortConnect=false",
                ["RateLimiting:Redis:Policies:public-search:PermitLimit"] = "500",
                ["RateLimiting:Redis:Policies:public-search:WindowSeconds"] = "5",
                ["RateLimiting:Redis:Policies:auth-login:PermitLimit"] = "50",
                ["RateLimiting:Redis:Policies:auth-login:WindowSeconds"] = "60",
                ["RateLimiting:Redis:Policies:public-read:PermitLimit"] = "120",
                ["RateLimiting:Redis:Policies:public-read:WindowSeconds"] = "60",
                ["RateLimiting:Redis:Policies:public-read:InProcess"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHudhudNestApiRedisRateLimiting(configuration, new TestHostEnvironment());
        var options = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisRateLimitingOptions>>().Value;

        Assert.Equal(500, options.Policies["public-search"].PermitLimit);
        Assert.True(options.Policies["public-search"].InProcess);
        Assert.NotEqual(true, options.Policies["auth-login"].InProcess);
        // An explicit setting wins over the default.
        Assert.False(options.Policies["public-read"].InProcess);
    }

    [Fact(DisplayName = "An in-process policy allows the limit without Redis and then answers 429, not 503")]
    public async Task InProcessPolicy_WithoutRedis_LimitsWith429()
    {
        var counter = new InProcessFixedWindowCounter();
        var nextCalls = 0;
        var middleware = CreateMiddleware(_ => { nextCalls++; return Task.CompletedTask; }, counter);

        for (var i = 0; i < 3; i++)
        {
            var ok = CreateHttpContext("public-search", "203.0.113.10");
            await middleware.InvokeAsync(ok);
            Assert.Equal(StatusCodes.Status200OK, ok.Response.StatusCode);
        }

        var rejected = CreateHttpContext("public-search", "203.0.113.10");
        await middleware.InvokeAsync(rejected);

        Assert.Equal(3, nextCalls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.True(rejected.Response.Headers.ContainsKey("Retry-After"));

        // Another client has its own budget.
        var other = CreateHttpContext("public-search", "203.0.113.11");
        await middleware.InvokeAsync(other);
        Assert.Equal(4, nextCalls);
    }

    [Fact(DisplayName = "A Redis policy still fails closed with 503 when Redis is unavailable")]
    public async Task RedisPolicy_WithoutRedis_StillFailsClosed()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, new InProcessFixedWindowCounter());

        var context = CreateHttpContext("auth-login", "203.0.113.10");
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact(DisplayName = "The counter starts again in a new window")]
    public void Counter_NewWindow_StartsAtOne()
    {
        var counter = new InProcessFixedWindowCounter();

        Assert.Equal(1, counter.Increment("k", bucket: 10, windowEndsAtMs: 11_000, nowMs: 10_100));
        Assert.Equal(2, counter.Increment("k", bucket: 10, windowEndsAtMs: 11_000, nowMs: 10_200));
        Assert.Equal(1, counter.Increment("k", bucket: 11, windowEndsAtMs: 12_000, nowMs: 11_100));
        // A late reader of the old window counts in the newer one instead of resetting it.
        Assert.Equal(2, counter.Increment("k", bucket: 10, windowEndsAtMs: 11_000, nowMs: 10_999));
    }

    [Fact(DisplayName = "Ended windows are swept, so memory stays bounded")]
    public void Counter_SweepsEndedWindows()
    {
        var counter = new InProcessFixedWindowCounter();
        for (var i = 0; i < 5000; i++)
            counter.Increment($"old-{i}", bucket: 1, windowEndsAtMs: 2_000, nowMs: 1_500);

        for (var i = 0; i < 5000; i++)
            counter.Increment($"new-{i}", bucket: 2, windowEndsAtMs: 3_000, nowMs: 2_500);

        Assert.Equal(5000, counter.EntryCount);
    }

    [Fact(DisplayName = "Concurrent requests are counted exactly")]
    public async Task Counter_ConcurrentIncrements_AreExact()
    {
        var counter = new InProcessFixedWindowCounter();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                counter.Increment("k", bucket: 5, windowEndsAtMs: 6_000, nowMs: 5_500);
        })));

        Assert.Equal(8001, counter.Increment("k", bucket: 5, windowEndsAtMs: 6_000, nowMs: 5_500));
    }

    private static RedisRateLimitingMiddleware CreateMiddleware(RequestDelegate next, InProcessFixedWindowCounter counter)
    {
        var options = new RedisRateLimitingOptions
        {
            Enabled = true,
            InstanceName = "UnitTest:",
            Policies = new Dictionary<string, RedisFixedWindowRateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["public-search"] = new() { PermitLimit = 3, WindowSeconds = 3600, InProcess = true },
                ["auth-login"] = new() { PermitLimit = 10, WindowSeconds = 60 }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(counter);

        return new RedisRateLimitingMiddleware(
            next,
            services.BuildServiceProvider(),
            Microsoft.Extensions.Options.Options.Create(options),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment(),
            NullLogger<RedisRateLimitingMiddleware>.Instance);
    }

    private static HttpContext CreateHttpContext(string policy, string ip)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        httpContext.Response.Body = new MemoryStream();
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)),
            "test-endpoint"));
        return httpContext;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = nameof(InProcessPublicReadRateLimitTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
