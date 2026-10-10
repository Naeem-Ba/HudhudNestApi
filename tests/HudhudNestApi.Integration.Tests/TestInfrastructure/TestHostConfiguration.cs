using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using InfrastructureApplicationRole =
    HudhudNestApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace HudhudNestApi.Integration.Tests.TestInfrastructure;

internal static class TestHostConfiguration
{
    public static void UseStableTestLogging(this IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
        });
    }

    public static void UseEphemeralDataProtection(
        this IServiceCollection services)
    {
        services.RemoveAll<IDataProtectionProvider>();
        services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider());
    }

    /// <summary>
    /// Swaps the real Npgsql-backed <see cref="AppDbContext"/> registration for the InMemory provider.
    /// Since EF Core 9 <c>AddDbContext</c> records its provider callback as an
    /// <c>IDbContextOptionsConfiguration&lt;TContext&gt;</c> next to <c>DbContextOptions&lt;TContext&gt;</c>;
    /// removing only the options leaves the Npgsql callback behind, and EF then refuses to build
    /// the context with "Services for database providers ... have been registered".
    /// </summary>
    public static void UseInMemoryAppDbContext(
        this IServiceCollection services,
        string databaseName)
    {
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
    }

    public static void AddDataProtectionSettings(
        IDictionary<string, string?> settings,
        string scopeName)
    {
        var keysPath = CreateDataProtectionKeysPath(scopeName);

        settings["DataProtection:KeysPath"] = keysPath;
        settings["DataProtection:PersistKeysToDatabase"] = "false";
    }

    public static void SeedApplicationRoles(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager =
            scope.ServiceProvider.GetRequiredService<
                RoleManager<InfrastructureApplicationRole>>();

        ApplicationRolesSeed.SeedAsync(roleManager)
            .GetAwaiter()
            .GetResult();
    }

    private static string CreateDataProtectionKeysPath(string scopeName)
    {
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "HudhudNestApiTests",
            Sanitize(scopeName),
            "DataProtectionKeys");

        Directory.CreateDirectory(keysPath);

        return keysPath;
    }

    private static string Sanitize(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();

        return string.Concat(
            value.Select(ch =>
                invalidChars.Contains(ch)
                    ? '_'
                    : ch));
    }
}
