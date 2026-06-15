using System.Net;

namespace PropertyApi.Security.RateLimiting;

internal static class RedisRateLimitPartitionKeyResolver
{
    public static string Resolve(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;

        if (remoteIp is null)
            return "ip:unknown";

        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        return $"ip:{remoteIp}";
    }
}
