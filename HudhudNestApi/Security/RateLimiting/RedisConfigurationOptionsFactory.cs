using StackExchange.Redis;

namespace HudhudNestApi.Security.RateLimiting;

// Shared by every caller that needs to open its own StackExchange.Redis connection from the
// app's resolved connection string (currently: the general-purpose IConnectionMultiplexer
// registered in Program.cs, and the rate-limiting-specific one in
// RedisRateLimitingServiceCollectionExtensions) so the redis:// URI parsing / Upstash SSL
// detection logic lives in exactly one place.
internal static class RedisConfigurationOptionsFactory
{
    public static ConfigurationOptions Build(string connectionString)
    {
        if (connectionString.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFromUri(connectionString);
        }

        var options = ConfigurationOptions.Parse(connectionString);

        options.AbortOnConnectFail = false;
        options.ConnectRetry = 3;
        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;

        return options;
    }

    private static ConfigurationOptions BuildFromUri(string connectionString)
    {
        var uri = new Uri(connectionString);

        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectRetry = 3,
            ConnectTimeout = 5000,
            SyncTimeout = 5000,

            // Upstash غالبًا يحتاج SSL.
            // rediss:// يعني SSL صراحة.
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.Contains("upstash.io", StringComparison.OrdinalIgnoreCase)
        };

        var port = uri.Port > 0 ? uri.Port : 6379;
        options.EndPoints.Add(uri.Host, port);

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfoParts = uri.UserInfo.Split(':', 2);

            if (userInfoParts.Length == 2)
            {
                options.User = Uri.UnescapeDataString(userInfoParts[0]);
                options.Password = Uri.UnescapeDataString(userInfoParts[1]);
            }
            else
            {
                options.Password = Uri.UnescapeDataString(userInfoParts[0]);
            }
        }

        return options;
    }
}
