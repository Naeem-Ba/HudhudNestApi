using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Launch audit A6-002: public responses are cached in memory by default, even in Production with
/// a Redis connection string, so public GETs cost no Upstash commands. Redis is opt-in for
/// several instances.
/// </summary>
public sealed class OutputCacheStoreSelectionTests
{
    [Theory(DisplayName = "Without OutputCache:Store the memory store is used, even in Production with Redis")]
    [InlineData(null)]
    [InlineData("Memory")]
    [InlineData("memory")]
    public void DefaultOrMemory_UsesMemoryStore(string? store)
    {
        var name = ResolveStoreTypeName("Production", store, redis: "redis.invalid:6379,abortConnect=false");
        Assert.DoesNotContain("Redis", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "OutputCache:Store=Redis uses the Redis store")]
    public void Redis_UsesRedisStore()
    {
        var name = ResolveStoreTypeName("Production", "Redis", redis: "redis.invalid:6379,abortConnect=false");
        Assert.Contains("Redis", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "OutputCache:Store=Redis without a connection string fails at startup")]
    public void Redis_WithoutConnection_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ResolveStoreTypeName("Production", "Redis", redis: null));
    }

    [Fact(DisplayName = "An unknown OutputCache:Store value fails at startup")]
    public void UnknownStore_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ResolveStoreTypeName("Production", "Disk", redis: null));
    }

    private static string ResolveStoreTypeName(string environment, string? store, string? redis)
    {
        var values = new Dictionary<string, string?>();
        if (store is not null) values["OutputCache:Store"] = store;
        if (redis is not null) values["ConnectionStrings:Redis"] = redis;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScaleOutOutputCaching(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new TestHostEnvironment(environment));

        return services.BuildServiceProvider().GetRequiredService<IOutputCacheStore>().GetType().Name;
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = nameof(OutputCacheStoreSelectionTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
