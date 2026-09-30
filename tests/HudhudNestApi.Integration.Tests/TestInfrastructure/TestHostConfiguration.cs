using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
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
