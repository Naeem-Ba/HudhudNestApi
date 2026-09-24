using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Security.RateLimiting;

namespace PropertyApi.Integration.Tests.Security;

/// <summary>
/// Program.cs mounts either the Redis limiter or the in-memory one, never both, so the choice has to agree
/// with what the Redis middleware itself reads per request. It used to come from the pre-build configuration:
/// under CI (RateLimiting__Redis__Enabled=true for the whole process) a test host that switched Redis off in
/// its own settings got the Redis middleware, which then passed every request because its options said
/// disabled -- no limiter ran at all (PhoneLoginAuditTests H1-H3 red on master, run 35918522176).
/// </summary>
public sealed class RedisRateLimitingActivationTests
{
    [Fact(DisplayName = "Registered and enabled: the Redis limiter runs")]
    public void RegisteredAndEnabled_IsActive()
    {
        var services = Build(registered: true, finalEnabled: "true");

        Assert.True(services.IsRedisRateLimitingActive());
    }

    [Fact(DisplayName = "Registered at startup but switched off by the final configuration: the in-memory limiter runs")]
    public void RegisteredButDisabledByLaterConfiguration_IsNotActive()
    {
        var services = Build(registered: true, finalEnabled: "false");

        Assert.False(services.IsRedisRateLimitingActive());
    }

    [Fact(DisplayName = "Never registered: the in-memory limiter runs even though IOptions defaults to Enabled")]
    public void NotRegistered_IsNotActive()
    {
        var services = Build(registered: false, finalEnabled: "true");

        Assert.False(services.IsRedisRateLimitingActive());
    }

    private static ServiceProvider Build(bool registered, string finalEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379",
                [$"{RedisRateLimitingOptions.SectionName}:Enabled"] = finalEnabled
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions();
        if (registered)
            services.AddPropertyApiRedisRateLimiting(configuration, new TestHostEnvironment());

        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = nameof(RedisRateLimitingActivationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
