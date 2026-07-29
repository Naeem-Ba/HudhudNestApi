using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;
using InfrastructureApplicationRole =
    PropertyApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace PropertyApi.Integration.Tests.Auth;

internal sealed class PostgresAuthTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly AuthFaultPlan _faults;
    private readonly IReadOnlyCollection<SocialIdentitySeed> _socialSeeds;

    public PostgresAuthTestFactory(
        AuthFaultPlan? faults = null,
        IEnumerable<SocialIdentitySeed>? socialSeeds = null)
    {
        _connectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "A PostgreSQL test connection string is required. Set " +
                "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");

        _faults = faults ?? new AuthFaultPlan();
        _socialSeeds = socialSeeds?.ToArray() ?? Array.Empty<SocialIdentitySeed>();
        Sms = new SmsCaptureSink();
    }

    public SmsCaptureSink Sms { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] =
            _connectionString,

        ["RateLimiting:Redis:Enabled"] =
            "false",

        ["DataProtection:PersistKeysToDatabase"] =
            "true",

        ["OtpSettings:SecretKey"] =
            "integration-test-otp-secret-key-at-least-32-bytes",

        ["Security:PhoneLookupHmacKey"] =
            "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
    });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(
                new EphemeralDataProtectionProvider());

            services.DecorateAuthServices(_faults);

            if (_socialSeeds.Count > 0)
            {
                services.RemoveAll<ISocialTokenVerifier>();

                foreach (var providerGroup in _socialSeeds.GroupBy(x => x.ProviderName, StringComparer.OrdinalIgnoreCase))
                {
                    var verifier = new TestSocialTokenVerifier(providerGroup.Key, providerGroup);
                    services.AddSingleton<ISocialTokenVerifier>(verifier);
                }
            }

            SmsCaptureRegistration.ReplaceSmsAbstractions(services, Sms);
        });
    }

    public async Task PrepareDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();
        await TruncateMutableTablesAsync(db);
        await EnsureUserRoleAsync(scope.ServiceProvider);
        Sms.Clear();
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    public async Task<IdentityAccountSnapshot> SeedUserAsync(
        string email,
        string? phoneNumber = null,
        bool emailConfirmed = true,
        bool phoneConfirmed = false,
        bool addUserRole = true)
    {
        return await InScopeAsync(
            async services =>
            {
                var registrationIdentity =
                    services.GetRequiredService<
                        IRegisterIdentityService>();

                var userIdentity =
                    services.GetRequiredService<
                        IUserIdentityReadService>();

                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                var normalized =
                    email
                        .Trim()
                        .ToLowerInvariant();

                var now =
                    DateTime.UtcNow;

                var identityId =
                    Guid.NewGuid();

                var createRequest =
                    new CreateIdentityAccount(
                        UserAccountId:
                            identityId,

                        Email:
                            normalized,

                        PhoneNumber:
                            phoneNumber,

                        Password:
                            "StrongPass!123",

                        LegacyFirstName:
                            "Existing",

                        LegacyLastName:
                            "User",

                        LegacyCreatedAtUtc:
                            now,

                        EmailConfirmed:
                            emailConfirmed,

                        LegacyProfileImageUrl:
                            null,

                        PhoneConfirmed:
                            phoneConfirmed);

                var createResult =
                    await registrationIdentity.CreateAsync(
                        createRequest,
                        CancellationToken.None);

                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Failed to seed integration-test identity.");
                }

                /*
                 * Test-fixture invariant:
                 *
                 * IdentityId == UserAccountId
                 *
                 * Existing identities used by integration tests must
                 * have the same profile projection guaranteed by the
                 * production migration/backfill contract.
                 */
                var account =
                    UserAccount.Create(
                        identityId,
                        "Existing",
                        "User",
                        now);

                db.UserAccounts.Add(
                    account);

                await db.SaveChangesAsync(
                    CancellationToken.None);

                if (addUserRole)
                {
                    var roleResult =
                        await registrationIdentity.AddToRoleAsync(
                            identityId,
                            RoleNames.User,
                            CancellationToken.None);

                    if (!roleResult.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Failed to seed integration-test identity role.");
                    }
                }

                return await userIdentity.FindByIdAsync(
                           identityId,
                           CancellationToken.None)
                       ?? throw new InvalidOperationException(
                           "Seeded integration-test identity could not be reloaded.");
            });
    }


    private static async Task EnsureUserRoleAsync(IServiceProvider services)
    {
        var roleManager =
    services.GetRequiredService<
        RoleManager<InfrastructureApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(RoleNames.User))
        {
            var result = await roleManager.CreateAsync(
     new InfrastructureApplicationRole
     {
         Id = Guid.NewGuid(),
         Name = RoleNames.User
     });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not seed role '{RoleNames.User}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    private static async Task TruncateMutableTablesAsync(AppDbContext db)
    {
        var roleEntity = db.Model.FindEntityType(
            typeof(InfrastructureApplicationRole));

        var roleTable = roleEntity?.GetTableName();
        var roleSchema = roleEntity?.GetSchema() ?? "public";

        var modelTables = db.Model.GetEntityTypes()
            .Select(e => new
            {
                Table = e.GetTableName(),
                Schema = e.GetSchema() ?? "public"
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Table))
            .Distinct()
            .Where(x => !string.Equals(x.Table, roleTable, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(x.Schema, roleSchema, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x.Table, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (modelTables.Length == 0)
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            var existingTables = new HashSet<string>(StringComparer.Ordinal);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT table_schema, table_name
                    FROM information_schema.tables
                    WHERE table_type = 'BASE TABLE'
                      AND table_schema NOT IN ('pg_catalog', 'information_schema');
                    """;

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingTables.Add(TableKey(reader.GetString(0), reader.GetString(1)));
                }
            }

            var tablesToTruncate = modelTables
                .Where(x => existingTables.Contains(TableKey(x.Schema, x.Table!)))
                .Select(x => QuoteTable(x.Schema, x.Table!))
                .ToArray();

            if (tablesToTruncate.Length == 0)
            {
                return;
            }

            var sql =
                $"TRUNCATE TABLE {string.Join(", ", tablesToTruncate)} RESTART IDENTITY CASCADE;";

            await db.Database.ExecuteSqlRawAsync(sql);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string TableKey(string schema, string table)
        => $"{schema}\u001F{table}";

    private static string QuoteTable(string? schema, string table)
    {
        static string Q(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
        return string.IsNullOrWhiteSpace(schema) ? Q(table) : $"{Q(schema)}.{Q(table)}";
    }
}
