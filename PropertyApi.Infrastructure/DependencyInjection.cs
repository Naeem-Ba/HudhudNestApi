using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Infrastructure.Auth;
using PropertyApi.Infrastructure.Caching;
using PropertyApi.Infrastructure.Health;
using PropertyApi.Infrastructure.Lookups;
using PropertyApi.Infrastructure.Media;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Repositories;
using PropertyApi.Infrastructure.Services;

namespace PropertyApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ConfigureDataProtectionKeyStorage(services, configuration, environment);
        ConfigureDataProtection(services, configuration, environment);

        services.AddAuthInfrastructure(configuration, environment);
        services.AddPersistenceInfrastructure(configuration, environment);
        services.AddRepositoryInfrastructure();
        services.AddLookupInfrastructure();
        services.AddUserContextInfrastructure();
        services.AddMediaInfrastructure(configuration, environment);
        services.AddCacheInfrastructure(configuration, environment);
        services.AddOperationalHealthChecks();

        return services;
    }

    private static void ConfigureDataProtectionKeyStorage(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!ShouldPersistDataProtectionKeysToDatabase(configuration, environment))
        {
            return;
        }

        services.AddDbContext<DataProtectionKeyDbContext>(options =>
        {
            var connectionString =
                PostgresConnectionStringResolver.Resolve(
                    configuration,
                    environment);

            options.UseNpgsql(connectionString);
        });
    }

    private static void ConfigureDataProtection(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keysPath = configuration["DataProtection:KeysPath"]
            ?? Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH");

        var builder = services.AddDataProtection()
            .SetApplicationName("PropertyApi");

        if (ShouldPersistDataProtectionKeysToDatabase(configuration, environment))
        {
            builder.PersistKeysToDbContext<DataProtectionKeyDbContext>();
            return;
        }

        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            var directory = new DirectoryInfo(keysPath);
            directory.Create();
            builder.PersistKeysToFileSystem(directory);
            return;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Data Protection key persistence is required in Production. " +
                "Set DataProtection:PersistKeysToDatabase=true or configure DATA_PROTECTION_KEYS_PATH/DataProtection:KeysPath.");
        }
    }

    private static bool ShouldPersistDataProtectionKeysToDatabase(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredValue = configuration["DataProtection:PersistKeysToDatabase"]
            ?? Environment.GetEnvironmentVariable("DATA_PROTECTION_PERSIST_KEYS_TO_DATABASE");

        if (bool.TryParse(configuredValue, out var configured))
        {
            return configured;
        }

        return environment.IsProduction();
    }
}
