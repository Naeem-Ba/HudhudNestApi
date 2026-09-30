using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Contracts;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Integration.Tests.AppUpdates;

namespace HudhudNestApi.Integration.Tests.Messaging;

/// <summary>
/// Real HTTP-lifecycle fixture for owner messaging and owner rating, same shape as
/// AppUpdateApiTestFactory — boots the actual Program host against an isolated PostgreSQL
/// instance and mints real access tokens through the real ITokenService/Identity stack, so
/// authorization is exercised exactly as in production (the caller is always the token's user).
/// </summary>
public sealed class MessagingApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public MessagingApiTestFactory()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "A PostgreSQL test connection string is required. Set " +
                "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["RateLimiting:Redis:Enabled"] = "false",
                ["DataProtection:PersistKeysToDatabase"] = "true",
                ["OtpSettings:SecretKey"] = "integration-test-otp-secret-key-at-least-32-bytes",
                ["Security:PhoneLookupHmacKey"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        });
    }

    /// <summary>See InvestmentApiTestFactory.PrepareDatabaseAsync's remarks — re-runs the
    /// idempotent reference-data seeder after migrations to close the same first-run race.</summary>
    public async Task PrepareDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
        await TruncateMutableTablesAsync(db);
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

    public Task<SeededUser> SeedUserAsync(string emailPrefix, params string[] roles) =>
        InScopeAsync(async services =>
        {
            var registrationIdentity = services.GetRequiredService<IRegisterIdentityService>();
            var userIdentity = services.GetRequiredService<IUserIdentityReadService>();
            var tokenService = services.GetRequiredService<ITokenService>();
            var db = services.GetRequiredService<AppDbContext>();

            var email = $"{emailPrefix}.{Guid.NewGuid():N}@messaging-tests.local".ToLowerInvariant();
            var now = DateTime.UtcNow;
            var identityId = Guid.NewGuid();

            var createResult = await registrationIdentity.CreateAsync(
                new CreateIdentityAccount(
                    UserAccountId: identityId,
                    Email: email,
                    PhoneNumber: null,
                    Password: "StrongPass!123",
                    LegacyFirstName: "Test",
                    LegacyLastName: "User",
                    LegacyCreatedAtUtc: now,
                    EmailConfirmed: true,
                    LegacyProfileImageUrl: null,
                    PhoneConfirmed: false),
                CancellationToken.None);

            if (!createResult.Succeeded)
                throw new InvalidOperationException("Failed to seed integration-test identity.");

            db.UserAccounts.Add(UserAccount.Create(identityId, "Test", "User", now));
            await db.SaveChangesAsync(CancellationToken.None);

            foreach (var role in roles)
            {
                var roleResult = await registrationIdentity.AddToRoleAsync(identityId, role, CancellationToken.None);
                if (!roleResult.Succeeded)
                    throw new InvalidOperationException($"Failed to add role '{role}' to seeded test identity.");
            }

            var snapshot = await userIdentity.FindByIdAsync(identityId, CancellationToken.None)
                ?? throw new InvalidOperationException("Seeded integration-test identity could not be reloaded.");

            var token = tokenService.GenerateAccessToken(
                new AccessTokenSubject(identityId, email, email, snapshot.SecurityStamp),
                roles);

            return new SeededUser(identityId, email, token);
        });

    /// <summary>Seeds a published listing owned by <paramref name="ownerId"/> directly (bypassing HTTP).</summary>
    public Task<Guid> SeedPropertyAsync(Guid ownerId, string title = "شقة للإيجار") =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = Property.Create(title, "وصف العقار", ownerId, ListingType.ForRent);
            db.Properties.Add(property);
            await db.SaveChangesAsync(CancellationToken.None);
            return property.Id;
        });

    public HttpClient AuthedClient(string? token = null)
    {
        var client = CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Same routine as AppUpdateApiTestFactory's — truncates every mapped table except
    /// the shared Roles table and __EFMigrationsHistory.</summary>
    private static async Task TruncateMutableTablesAsync(AppDbContext db)
    {
        var roleEntity = db.Model.FindEntityType(typeof(HudhudNestApi.Infrastructure.Identity.Entities.ApplicationRole));
        var roleTable = roleEntity?.GetTableName();
        var roleSchema = roleEntity?.GetSchema() ?? "public";

        var modelTables = db.Model.GetEntityTypes()
            .Select(e => new { Table = e.GetTableName(), Schema = e.GetSchema() ?? "public" })
            .Where(x => !string.IsNullOrWhiteSpace(x.Table))
            .Distinct()
            .Where(x => !string.Equals(x.Table, roleTable, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(x.Schema, roleSchema, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x.Table, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (modelTables.Length == 0)
            return;

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync();

        try
        {
            var existingTables = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT table_schema, table_name
                    FROM information_schema.tables
                    WHERE table_type = 'BASE TABLE'
                      AND table_schema NOT IN ('pg_catalog', 'information_schema');
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    existingTables.Add($"{reader.GetString(0)}{reader.GetString(1)}");
            }

            var tablesToTruncate = modelTables
                .Where(x => existingTables.Contains($"{x.Schema}{x.Table}"))
                .Select(x => $"\"{x.Schema}\".\"{x.Table!.Replace("\"", "\"\"")}\"")
                .ToArray();

            if (tablesToTruncate.Length == 0)
                return;

            var sql = $"TRUNCATE TABLE {string.Join(", ", tablesToTruncate)} RESTART IDENTITY CASCADE;";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
