using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Configuration;
using PropertyApi.Security.Staging;

namespace PropertyApi.Integration.Tests.Security;

/// <summary>
/// Directly unit-tests RateLimitingRegistration.BuildOtpPolicyPartition -- the method the
/// "send-otp"/"verify-otp" AddPolicy lambdas in that file now delegate to (see its own doc
/// comment for why: the lambdas capture nothing but static-method calls, so the compiler caches
/// each as a single static delegate, and ASP.NET Core's rate limiter middleware itself only
/// invokes a policy's factory once per distinct partition key -- calling this method directly
/// removes any dependency on that invocation/caching timing, unlike
/// StagingSmokeRateLimitExemptionTests (same behavior, exercised end-to-end over real HTTP).
/// </summary>
public sealed class RateLimitingRegistrationTests
{
    [Fact(DisplayName = "BuildOtpPolicyPartition returns an unlimited partition for a valid Staging smoke secret")]
    public void BuildOtpPolicyPartition_WithValidStagingSmokeSecret_IsUnlimited()
    {
        var httpContext = CreateStagingHttpContext(secretHeaderValue: ValidSecret);

        var partition = RateLimitingRegistration.BuildOtpPolicyPartition(
            httpContext, permitLimit: 3, window: TimeSpan.FromMinutes(15));

        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 20; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired, $"Attempt {i + 1} was unexpectedly rejected for exempt traffic.");
        }
    }

    [Fact(DisplayName = "BuildOtpPolicyPartition still enforces the permit limit without a Staging smoke secret")]
    public void BuildOtpPolicyPartition_WithoutSecret_EnforcesPermitLimit()
    {
        var httpContext = CreateStagingHttpContext(secretHeaderValue: null);

        var partition = RateLimitingRegistration.BuildOtpPolicyPartition(
            httpContext, permitLimit: 3, window: TimeSpan.FromMinutes(15));

        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired, $"Attempt {i + 1} of 3 (within the permit limit) was unexpectedly rejected.");
        }

        using var overLimitLease = limiter.AttemptAcquire(1);
        Assert.False(overLimitLease.IsAcquired, "The 4th attempt (over PermitLimit=3) should have been rejected.");
    }

    [Fact(DisplayName = "BuildOtpPolicyPartition ignores an incorrect Staging smoke secret and still enforces the permit limit")]
    public void BuildOtpPolicyPartition_WithWrongSecret_EnforcesPermitLimit()
    {
        var httpContext = CreateStagingHttpContext(secretHeaderValue: "not-the-real-secret");

        var partition = RateLimitingRegistration.BuildOtpPolicyPartition(
            httpContext, permitLimit: 3, window: TimeSpan.FromMinutes(15));

        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired);
        }

        using var overLimitLease = limiter.AttemptAcquire(1);
        Assert.False(overLimitLease.IsAcquired, "An incorrect secret must not exempt the caller.");
    }

    [Fact(DisplayName = "BuildOtpPolicyPartition enforces the permit limit outside Staging even with a matching secret")]
    public void BuildOtpPolicyPartition_OutsideStaging_EnforcesPermitLimitEvenWithSecret()
    {
        var httpContext = CreateHttpContext(environmentName: "Production", secretHeaderValue: ValidSecret);

        var partition = RateLimitingRegistration.BuildOtpPolicyPartition(
            httpContext, permitLimit: 3, window: TimeSpan.FromMinutes(15));

        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired);
        }

        using var overLimitLease = limiter.AttemptAcquire(1);
        Assert.False(overLimitLease.IsAcquired, "The exemption must never apply outside Staging.");
    }

    private const string ValidSecret = "unit-test-staging-smoke-secret-at-least-32-characters";

    private static HttpContext CreateStagingHttpContext(string? secretHeaderValue) =>
        CreateHttpContext(environmentName: "Staging", secretHeaderValue);

    private static HttpContext CreateHttpContext(string environmentName, string? secretHeaderValue)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Staging:TestSupport:Enabled"] = "true",
                ["Staging:TestSupport:CleanupSecret"] = ValidSecret
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

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
        public string ApplicationName { get; set; } = nameof(RateLimitingRegistrationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
