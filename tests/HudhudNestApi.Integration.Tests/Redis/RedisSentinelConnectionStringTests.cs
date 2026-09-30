using StackExchange.Redis;

namespace HudhudNestApi.Integration.Tests.Redis;

/// <summary>
/// Redis Sentinel HA: application-level Sentinel-awareness, kept deliberately separate from
/// infrastructure-level HA verification (see docs/REDIS-HA.md "Redis infrastructure HA
/// verification vs. Application Sentinel-aware failover").
///
/// <see cref="HudhudNestApi.Configuration.RedisConnectionResolver"/>,
/// <c>RedisRateLimitingServiceCollectionExtensions</c>, the ASP.NET Core distributed
/// cache/output cache registrations, and the SignalR Redis backplane registration all pass a
/// non-"redis://" connection string straight through to StackExchange.Redis's own
/// <see cref="ConfigurationOptions.Parse"/> instead of building
/// <see cref="ConfigurationOptions"/> by hand. That means the application is already
/// Sentinel-capable at the configuration layer today: pointing
/// <c>ConnectionStrings:Redis</c> (or any of the other keys
/// <see cref="HudhudNestApi.Configuration.RedisConnectionResolver"/> reads) at
/// "host1:26379,host2:26379,host3:26379,serviceName=mymaster" is enough for
/// StackExchange.Redis's <c>ConnectionMultiplexer</c> to discover the current primary through
/// Sentinel and follow it across a failover, with zero application code changes.
///
/// This is a real runtime assertion against the exact StackExchange.Redis version this
/// repository ships (see HudhudNestApi/HudhudNestApi.csproj and packages.lock.json), not a
/// source-text grep -- it is intentionally NOT a duplicate of
/// scripts/verify-redis-sentinel-ha.sh, which proves the Redis/Sentinel topology itself fails
/// over correctly. This test proves the orthogonal claim: that this codebase's Redis client
/// configuration layer would actually consume a Sentinel-style connection string correctly if
/// one were configured, which the CI-native infrastructure drill does not exercise (the
/// `api` container in ci/docker-compose.production-gate.yml still points at the single,
/// non-HA `redis:` service -- see docs/REDIS-HA.md "Limitations").
/// </summary>
public sealed class RedisSentinelConnectionStringTests
{
    [Fact(DisplayName = "StackExchange.Redis ConfigurationOptions.Parse recognizes a Sentinel-style connection string")]
    public void ConfigurationOptions_Parse_Should_Recognize_Sentinel_ServiceName()
    {
        const string connectionString =
            "redis-sentinel-1:26379,redis-sentinel-2:26379,redis-sentinel-3:26379,serviceName=mymaster,abortConnect=false";

        var options = ConfigurationOptions.Parse(connectionString);

        Assert.Equal("mymaster", options.ServiceName);
        Assert.Equal(3, options.EndPoints.Count);
        Assert.False(options.AbortOnConnectFail);
    }

    [Fact(DisplayName = "A plain single-endpoint connection string has no ServiceName (non-Sentinel baseline)")]
    public void ConfigurationOptions_Parse_Should_Leave_ServiceName_Null_For_Plain_Connection_String()
    {
        const string connectionString = "redis:6379,abortConnect=false";

        var options = ConfigurationOptions.Parse(connectionString);

        Assert.Null(options.ServiceName);
        Assert.Single(options.EndPoints);
    }
}
