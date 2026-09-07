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
using PropertyApi.Application.Common.Models;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Investments.Entities;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Persistence.Seeds;

namespace PropertyApi.Integration.Tests.Investments;

/// <summary>
/// Real HTTP-lifecycle fixture for the Investment Discovery module (Phase 2, §16-17). Boots the
/// actual <c>Program</c> host against an isolated PostgreSQL instance (never production), mints
/// access tokens through the real <see cref="ITokenService"/>/Identity stack so JwtBearer's
/// security-stamp validation (see JwtAuthenticationRegistration.OnTokenValidated) is exercised
/// for real, and fakes only <see cref="IMediaStorageService"/> (Cloudinary) since no live
/// provider credentials exist in this environment — everything else in the request pipeline
/// (routing, [Authorize], MediatR, EF Core/Postgres, FluentValidation) is the genuine article.
/// </summary>
public sealed class InvestmentApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InvestmentApiTestFactory()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "A PostgreSQL test connection string is required. Set " +
                "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");
    }

    public FakeMediaStorageService Media { get; } = new();

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

            services.RemoveAll<IMediaStorageService>();
            services.AddSingleton<IMediaStorageService>(Media);
        });
    }

    /// <summary>Applies pending migrations (no-op if already current) and truncates every
    /// mutable table so each test class starts from a clean, isolated slate — never touches a
    /// database this factory did not itself connect to via the injected connection string.</summary>
    ///
    /// <remarks>
    /// Accessing <see cref="Services"/> above builds and starts the WebApplicationFactory host,
    /// which runs <c>Program.cs</c>'s best-effort <c>SeedReferenceDataAsync()</c> (roles,
    /// governorates, etc.) *before* the <see cref="AppDbContext.Database.MigrateAsync"/> call
    /// below has created any schema — against a brand-new database that first-run seed attempt
    /// fails (tables don't exist yet) and is swallowed by design (it must not block the app from
    /// serving requests). Re-running the same idempotent seeder here, after migrations, closes
    /// that startup race so role-dependent fixtures (e.g. <see cref="SeedUserAsync"/> with an
    /// "ADMIN" role) don't depend on seeding having already succeeded by luck of test ordering
    /// against a shared database.
    /// </remarks>
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

    /// <summary>Seeds a real Identity user + UserAccount + role(s), then mints a real access
    /// token via the production <see cref="ITokenService"/> using that user's actual current
    /// SecurityStamp — the same value the JwtBearer pipeline validates on every request. This is
    /// not a bypass of authentication: it is the same signing/claims code path the API's own
    /// login endpoint uses, without re-deriving login/OTP behavior already covered by
    /// PropertyApi.Auth.Tests.</summary>
    public Task<SeededUser> SeedUserAsync(string emailPrefix, params string[] roles) =>
        InScopeAsync(async services =>
        {
            var registrationIdentity = services.GetRequiredService<IRegisterIdentityService>();
            var userIdentity = services.GetRequiredService<IUserIdentityReadService>();
            var tokenService = services.GetRequiredService<ITokenService>();
            var db = services.GetRequiredService<AppDbContext>();

            var email = $"{emailPrefix}.{Guid.NewGuid():N}@investment-tests.local".ToLowerInvariant();
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

    /// <summary>Minimal valid Property row to satisfy InvestmentProjects' FK — investments never
    /// duplicate Property fields, they reference this row (Phase 1 spec).</summary>
    public Task<Guid> SeedPropertyAsync(Guid ownerId) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = Property.Create(
                $"Test Property {Guid.NewGuid():N}", "Integration test property", ownerId, ListingType.ForSale);
            db.Properties.Add(property);
            await db.SaveChangesAsync(CancellationToken.None);
            return property.Id;
        });

    /// <summary>Seeds an InvestmentProject directly at an arbitrary lifecycle status by driving
    /// its real state-machine methods in sequence (never an arbitrary field assignment) — lets
    /// tests start from Approved/Published/etc. without re-exercising every HTTP workflow step
    /// for fixture setup.</summary>
    public Task<Guid> SeedInvestmentProjectAsync(
        Guid propertyId,
        Guid ownerUserId,
        InvestmentProjectStatus status = InvestmentProjectStatus.Draft,
        decimal targetAmount = 100_000m,
        decimal minimumInvestment = 1_000m) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var project = InvestmentProject.Create(
                propertyId, ownerUserId, $"Test Project {Guid.NewGuid():N}",
                "Integration test project description.", InvestmentProjectType.Residential);

            project.UpdateInvestmentParameters(
                targetAmount, minimumInvestment, null, "USD", 12, 5m, 10m);

            if (status is not InvestmentProjectStatus.Draft)
                project.SubmitForReview();
            if (status is InvestmentProjectStatus.Approved or InvestmentProjectStatus.Scheduled
                or InvestmentProjectStatus.Published or InvestmentProjectStatus.Suspended or InvestmentProjectStatus.Closed)
                project.Approve();
            if (status is InvestmentProjectStatus.Scheduled or InvestmentProjectStatus.Published
                or InvestmentProjectStatus.Suspended or InvestmentProjectStatus.Closed)
                project.Schedule(null);
            if (status is InvestmentProjectStatus.Published or InvestmentProjectStatus.Suspended or InvestmentProjectStatus.Closed)
                project.Publish();
            if (status is InvestmentProjectStatus.Suspended or InvestmentProjectStatus.Closed)
                project.Suspend();
            if (status is InvestmentProjectStatus.Closed)
                project.Close();
            if (status is InvestmentProjectStatus.Rejected)
                project.Reject("Integration test rejection reason.");

            db.InvestmentProjects.Add(project);
            await db.SaveChangesAsync(CancellationToken.None);
            return project.Id;
        });

    public Task SeedFinancialsAsync(Guid projectId) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var financials = InvestmentProjectFinancials.Create(projectId);
            financials.UpdateCosts(50_000m, 5_000m, 0m, 2_000m, 1_000m, 1_000m, 500m, 500m, 1_000m);
            db.InvestmentProjectFinancials.Add(financials);
            await db.SaveChangesAsync(CancellationToken.None);
        });

    public Task SeedRiskAsync(Guid projectId) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var risk = InvestmentRiskAssessment.Create(
                projectId, InvestmentRiskLevel.Medium, InvestmentRiskLevel.Medium, InvestmentRiskLevel.Low,
                InvestmentRiskLevel.Medium, InvestmentRiskLevel.Low, InvestmentRiskLevel.Medium,
                45, "Indicative, non-guaranteed risk summary for testing.");
            db.InvestmentRiskAssessments.Add(risk);
            await db.SaveChangesAsync(CancellationToken.None);
        });

    public Task<Guid> SeedDocumentAsync(Guid projectId, bool isPublic, InvestmentDocumentType type = InvestmentDocumentType.ProjectPlan) =>
        InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var document = InvestmentDocument.Create(
                projectId, type, "test-document.pdf", "fake", $"fake-{Guid.NewGuid():N}",
                $"https://fake.local/{Guid.NewGuid():N}.pdf", null, isPublic);
            db.InvestmentDocuments.Add(document);
            await db.SaveChangesAsync(CancellationToken.None);
            return document.Id;
        });

    public HttpClient AuthedClient(string? token = null)
    {
        var client = CreateClient();
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Same routine as PostgresAuthTestFactory's — truncates every mapped table except
    /// the shared Roles table (never re-seed roles per test) and __EFMigrationsHistory.</summary>
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
                    existingTables.Add($"{reader.GetString(0)}{reader.GetString(1)}");
            }

            var tablesToTruncate = modelTables
                .Where(x => existingTables.Contains($"{x.Schema}{x.Table}"))
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

/// <summary>In-memory stand-in for Cloudinary — no real provider credentials exist in this test
/// environment. Exercises the exact same IMediaStorageService contract AddInvestmentDocumentCommandHandler
/// depends on, so document-upload HTTP tests still verify real routing/auth/validation/EF
/// persistence; only the actual network call to Cloudinary is faked.</summary>
public sealed class FakeMediaStorageService : IMediaStorageService
{
    public Task<MediaUploadResult> UploadImageAsync(
        Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default) =>
        Task.FromResult(MediaUploadResult.Success($"https://fake.local/{folder}/{Guid.NewGuid():N}-{fileName}", $"fake-{Guid.NewGuid():N}"));

    public Task DeleteImageAsync(string publicId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<MediaFileResult?> GetImageAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<MediaFileResult?>(null);
}
