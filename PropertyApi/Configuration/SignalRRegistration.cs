namespace PropertyApi.Configuration;

public static class SignalRRegistration
{
    public static void AddPropertyApiSignalR(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string? redisConnectionString,
        bool hasRedisConnectionString)
    {
        var signalRBuilder = services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 16 * 1024;
            options.EnableDetailedErrors = environment.IsDevelopment();
        });

        var signalRProvider = configuration["SignalR:Provider"];
        var requireSignalRBackplane = configuration.GetValue<bool?>("SignalR:RequireBackplane") ?? false;

        if (string.Equals(signalRProvider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasRedisConnectionString)
            {
                throw new InvalidOperationException(
                    "SignalR Redis backplane is enabled, but no Redis connection string is configured.");
            }

            signalRBuilder.AddStackExchangeRedis(redisConnectionString!);
        }
        else if (string.Equals(signalRProvider, "AzureSignalR", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SignalR:Provider=AzureSignalR is configured, but Azure SignalR registration is not implemented in this build.");
        }
        else if (environment.IsProduction() && requireSignalRBackplane)
        {
            throw new InvalidOperationException(
                "SignalR backplane is required because SignalR:RequireBackplane=true. Configure SignalR:Provider=Redis or AzureSignalR.");
        }
    }
}
