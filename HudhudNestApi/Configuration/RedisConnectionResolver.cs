namespace HudhudNestApi.Configuration;

public static class RedisConnectionResolver
{
    /// <summary>
    /// Resolves the Redis connection string from whichever of the supported configuration
    /// keys is set, normalizes it into StackExchange.Redis format, and — when one is found —
    /// injects it back into <see cref="IConfiguration"/> under every key a downstream
    /// component reads (Infrastructure, RateLimiting, SignalR, IDistributedCache) so they all
    /// see the same valid value instead of falling back to localhost:6379.
    /// </summary>
    public static (string? ConnectionString, bool HasConnectionString) ResolveAndConfigure(
        WebApplicationBuilder builder)
    {
        var rawRedisConnectionString = GetRedisConnectionString(builder.Configuration);
        var redisConnectionString = NormalizeRedisConnectionString(rawRedisConnectionString);
        var hasRedisConnectionString = !string.IsNullOrWhiteSpace(redisConnectionString);

        if (hasRedisConnectionString)
        {
            // Normalize all Redis-related keys so Infrastructure, RateLimiting, SignalR,
            // and IDistributedCache read the same valid value instead of falling back to localhost:6379.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = redisConnectionString,
                ["Redis:ConnectionString"] = redisConnectionString,
                ["RedisRateLimiting:ConnectionString"] = redisConnectionString,
                ["SignalR:Redis:ConnectionString"] = redisConnectionString
            });
        }

        if (builder.Environment.IsProduction() && !hasRedisConnectionString)
        {
            throw new InvalidOperationException(
                "Redis is required in Production. Configure ConnectionStrings:Redis, Redis:ConnectionString, RedisRateLimiting:ConnectionString, REDIS_CONNECTION_STRING, or REDIS_URL.");
        }

        return (redisConnectionString, hasRedisConnectionString);
    }

    private static string? GetRedisConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"]
            ?? configuration["RedisRateLimiting:ConnectionString"]
            ?? configuration["REDIS_CONNECTION_STRING"]
            ?? configuration["REDIS_URL"];
    }

    internal static string? NormalizeRedisConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (!trimmed.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        // Uri.Port is -1 (not the scheme default) for redis:// and rediss://, which .NET does not
        // know, so a URL written without an explicit port would otherwise become "host:-1".
        var port = uri.Port > 0 ? uri.Port : 6379;

        var parts = new List<string>
        {
            $"{uri.Host}:{port}",
            "abortConnect=false",
            "connectRetry=3",
            "connectTimeout=5000",
            "syncTimeout=5000"
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfo = Uri.UnescapeDataString(uri.UserInfo);
            var separatorIndex = userInfo.IndexOf(':');
            var password = separatorIndex >= 0
                ? userInfo[(separatorIndex + 1)..]
                : userInfo;

            // "default" is Redis's implicit ACL user and is what password-only servers expect,
            // so it is left out; any other user is an ACL account and must be sent explicitly.
            var user = separatorIndex > 0 ? userInfo[..separatorIndex] : null;
            if (!string.IsNullOrWhiteSpace(user) &&
                !user.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"user={user}");
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                parts.Add($"password={password}");
            }
        }

        // Same TLS rule as RedisConfigurationOptionsFactory.BuildFromUri: Upstash only accepts TLS,
        // so a plain redis:// URL pointing at it still has to use ssl.
        if (uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("upstash.io", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("ssl=true");
        }

        var databaseText = uri.AbsolutePath.Trim('/');
        if (int.TryParse(databaseText, out var database))
        {
            parts.Add($"defaultDatabase={database}");
        }

        return string.Join(',', parts);
    }
}
