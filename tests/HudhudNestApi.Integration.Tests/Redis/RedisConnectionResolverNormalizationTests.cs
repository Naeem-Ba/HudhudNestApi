using HudhudNestApi.Configuration;
using StackExchange.Redis;

namespace HudhudNestApi.Integration.Tests.Redis;

/// <summary>
/// RedisConnectionResolver turns a redis:// / rediss:// URL (Upstash, Render and similar hand these out)
/// into the keyword form every downstream consumer (cache, rate limiting, SignalR) parses with
/// StackExchange.Redis. The result is checked by parsing it back, so a malformed endpoint fails here
/// instead of at the first request in a deployed environment.
/// </summary>
public sealed class RedisConnectionResolverNormalizationTests
{
    [Fact(DisplayName = "A redis URL without a port connects to the default Redis port, not port -1")]
    public void Url_Without_Port_Uses_6379()
    {
        var normalized = RedisConnectionResolver.NormalizeRedisConnectionString("redis://cache.internal");

        var options = ConfigurationOptions.Parse(normalized!);

        var endpoint = Assert.IsType<System.Net.DnsEndPoint>(options.EndPoints.Single());
        Assert.Equal(("cache.internal", 6379), (endpoint.Host, endpoint.Port));
    }

    [Fact(DisplayName = "An explicit port is kept")]
    public void Url_With_Port_Keeps_It()
    {
        var normalized = RedisConnectionResolver.NormalizeRedisConnectionString("rediss://default:s3cret@host.example:6380/2");

        var options = ConfigurationOptions.Parse(normalized!);

        var endpoint = Assert.IsType<System.Net.DnsEndPoint>(options.EndPoints.Single());
        Assert.Equal(("host.example", 6380), (endpoint.Host, endpoint.Port));
        Assert.True(options.Ssl);
        Assert.Equal("s3cret", options.Password);
        Assert.Equal(2, options.DefaultDatabase);
        Assert.Null(options.User);
    }

    [Fact(DisplayName = "A non-default ACL user is sent; the implicit 'default' user is not")]
    public void Acl_User_Is_Preserved()
    {
        var acl = ConfigurationOptions.Parse(
            RedisConnectionResolver.NormalizeRedisConnectionString("redis://app:pw@host:6379")!);
        var implicitUser = ConfigurationOptions.Parse(
            RedisConnectionResolver.NormalizeRedisConnectionString("redis://default:pw@host:6379")!);

        Assert.Equal("app", acl.User);
        Assert.Equal("pw", acl.Password);
        Assert.Null(implicitUser.User);
        Assert.Equal("pw", implicitUser.Password);
    }

    [Fact(DisplayName = "Upstash always uses TLS, even when the URL says redis://")]
    public void Upstash_Host_Forces_Ssl()
    {
        var options = ConfigurationOptions.Parse(
            RedisConnectionResolver.NormalizeRedisConnectionString("redis://default:pw@eu1-example-1234.upstash.io:6379")!);

        Assert.True(options.Ssl);
    }

    [Fact(DisplayName = "A keyword-format connection string passes through untouched")]
    public void Keyword_Format_Is_Not_Rewritten()
    {
        const string keyword = "redis:6379,abortConnect=false";

        Assert.Equal(keyword, RedisConnectionResolver.NormalizeRedisConnectionString(keyword));
    }
}
