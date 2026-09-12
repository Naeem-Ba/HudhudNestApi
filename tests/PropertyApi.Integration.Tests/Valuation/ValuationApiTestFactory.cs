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
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Lookups.Entities;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Persistence.Seeds;

namespace PropertyApi.Integration.Tests.Valuation;

/// <summary>
/// Stage 10 — real HTTP-lifecycle fixture for the Valuation module's security matrix. Mirrors
/// Investments' own InvestmentApiTestFactory exactly (real Program host, real Postgres, real
/// JwtBearer tokens minted through the production ITokenService/Identity stack) so [Authorize]/
/// [Authorize(Roles=...)] and every handler's own ownership check are exercised for real, not
/// bypassed by calling a controller method directly in-process.
/// </summary>
public sealed class ValuationApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ValuationApiTestFactory()
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

    /// <summary>Same race-closing double-seed reasoning InvestmentApiTestFactory's own doc
    /// comment documents for itself (roles/governorates must exist before role-dependent
    /// fixtures run, regardless of the app's own best-effort startup seed timing).</summary>
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

    /// <summary>Seeds a real Identity user + UserAccount + role(s) and mints a real access token
    /// via the production ITokenService — same approach InvestmentApiTestFactory.SeedUserAsync
    /// documents for itself.</summary>
    public Task<SeededUser> SeedUserAsync(string emailPrefix, params string[] roles) =>
        InScopeAsync(async services =>
        {
            var registrationIdentity = services.GetRequiredService<IRegisterIdentityService>();
            var userIdentity = services.GetRequiredService<IUserIdentityReadService>();
            var tokenService = services.GetRequiredService<ITokenService>();
            var db = services.GetRequiredService<AppDbContext>();

            var email = $"{emailPrefix}.{Guid.NewGuid():N}@valuation-security-tests.local".ToLowerInvariant();
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

    public Task<int> SeedGovernorateAsync() =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var governorate = Governorate.Create($"محافظة {Guid.NewGuid():N}", "Test Governorate");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();
            return governorate.Id;
        });

    /// <summary>Creates an agency owned by <paramref name="ownerUserId"/> AND joins that same
    /// user to it (Agency.OwnerUserId and UserAccount.AgencyId are two separate facts — see
    /// Agency's own doc comment — both are needed for ValuationOfficeInvitationsController's
    /// ownership check to resolve "my agency" for this user).</summary>
    public Task<Guid> SeedAgencyWithOwnerAsync(Guid ownerUserId) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var agency = Agency.Create($"مكتب اختبار {Guid.NewGuid():N}", $"test-office-{Guid.NewGuid():N}", ownerUserId, "SY", now);
            db.Agencies.Add(agency);

            var owner = await db.UserAccounts.FirstAsync(a => a.Id == ownerUserId);
            owner.JoinAgency(agency.Id, now);
            db.UserAccounts.Update(owner);

            await db.SaveChangesAsync();
            return agency.Id;
        });

    public Task<Guid> SeedInquiryAsync(int governorateId, Guid? requesterId = null) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var inquiry = ValuationInquiry.Create(governorateId, ListingType.ForSale, DateTime.UtcNow, requesterId: requesterId);
            inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow);
            db.ValuationInquiries.Add(inquiry);
            await db.SaveChangesAsync();
            return inquiry.Id;
        });

    /// <summary>A Sent (not yet responded) invitation — used for the "expired invitation must
    /// reject a response" scenario by seeding it already past its inquiry's ExpiresAt.</summary>
    public Task<Guid> SeedSentInvitationAsync(Guid inquiryId, Guid agencyId, DateTime sentAt) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var invitation = ValuationOfficeInvitation.Create(agencyId, inquiryId, ValuationMatchLevel.Neighborhood, sentAt);
            db.ValuationOfficeInvitations.Add(invitation);
            await db.SaveChangesAsync();
            return invitation.Id;
        });

    /// <summary>An already-Responded invitation, with its estimate persisted — the prerequisite
    /// state for the contact-consent scenario.</summary>
    public Task<Guid> SeedRespondedInvitationAsync(Guid inquiryId, Guid agencyId) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var invitation = ValuationOfficeInvitation.Create(agencyId, inquiryId, ValuationMatchLevel.Neighborhood, now);
            invitation.MarkResponded(now);
            var response = ValuationOfficeResponse.Create(invitation.Id, 100_000m, now);

            db.ValuationOfficeInvitations.Add(invitation);
            db.ValuationOfficeResponses.Add(response);
            await db.SaveChangesAsync();
            return invitation.Id;
        });

    public HttpClient AuthedClient(string? token = null)
    {
        var client = CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Same routine as InvestmentApiTestFactory's/PostgresAuthTestFactory's — truncates
    /// every mapped table except the shared Roles table (never re-seed roles per test) and
    /// __EFMigrationsHistory.</summary>
    private static async Task TruncateMutableTablesAsync(AppDbContext db)
    {
        var roleEntity = db.Model.FindEntityType(typeof(PropertyApi.Infrastructure.Identity.Entities.ApplicationRole));
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

public sealed record SeededUser(Guid Id, string Email, string AccessToken);
