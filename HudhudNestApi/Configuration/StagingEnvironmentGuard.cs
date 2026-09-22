using Npgsql;

namespace HudhudNestApi.Configuration;

public static class StagingEnvironmentGuard
{
    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsStaging())
            return;

        Require(configuration, "Deployment:CommitSha");
        Require(configuration, "Deployment:Version");
        Require(configuration, "Staging:EnvironmentId");
        Require(configuration, "Staging:DatabaseNameMarker");
        Require(configuration, "Staging:RedisIsolationMarker");
        Require(configuration, "Staging:StorageIsolationMarker");

        if (!configuration.GetValue<bool>("Staging:ExternalNotificationsDisabled"))
            throw new InvalidOperationException(
                "Staging:ExternalNotificationsDisabled must be true.");

        ValidateUrls(configuration);
        ValidateDatabase(configuration);
        ValidateRedis(configuration);

        if (configuration.GetValue<bool>("Staging:TestSupport:Enabled"))
        {
            Require(configuration, "Staging:TestSupport:FixedOtp");
            Require(configuration, "Staging:TestSupport:PhonePrefix");
            var cleanupSecret = Require(configuration, "Staging:TestSupport:CleanupSecret");
            if (cleanupSecret.Length < 32)
                throw new InvalidOperationException(
                    "Staging:TestSupport:CleanupSecret must contain at least 32 characters.");
        }
    }

    private static void ValidateUrls(IConfiguration configuration)
    {
        var staging = Require(configuration, "Deployment:PublicBaseUrl");
        var production = Require(configuration, "Deployment:ProductionBaseUrl");

        if (!Uri.TryCreate(staging, UriKind.Absolute, out var stagingUri))
            throw new InvalidOperationException(
                "Deployment:PublicBaseUrl must be an absolute HTTPS URL in Staging.");

        var allowHttpLocal = configuration.GetValue<bool>("Staging:AllowHttpLocal") &&
            (stagingUri.Host == "127.0.0.1" || stagingUri.Host == "localhost");
        if (stagingUri.Scheme != Uri.UriSchemeHttps && !allowHttpLocal)
            throw new InvalidOperationException(
                "Deployment:PublicBaseUrl must use HTTPS in Staging.");

        if (!Uri.TryCreate(production, UriKind.Absolute, out var productionUri))
            throw new InvalidOperationException(
                "Deployment:ProductionBaseUrl must be an absolute URL.");

        if (string.Equals(stagingUri.Host, productionUri.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Staging and Production hostnames must be different.");
    }

    private static void ValidateDatabase(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required in Staging.");
        var marker = Require(configuration, "Staging:DatabaseNameMarker");
        var database = new NpgsqlConnectionStringBuilder(raw).Database;

        if (string.IsNullOrWhiteSpace(database) ||
            !database.Contains(marker, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Staging database name does not contain the configured isolation marker.");
    }

    private static void ValidateRedis(IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(redis))
            throw new InvalidOperationException(
                "A dedicated Redis connection is required in Staging.");
    }

    private static string Require(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} is required in Staging.");
}
