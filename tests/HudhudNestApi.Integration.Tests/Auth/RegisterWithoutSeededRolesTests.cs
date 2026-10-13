using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// Production answered POST /api/auth/register with HTTP 500 ("An unexpected error occurred")
/// while login kept working. UserManager.AddToRoleAsync throws, rather than failing, when the
/// role row does not exist, and nothing guaranteed the row: the roles are seeded at startup in
/// the same transaction as the lookup data, and a failed seed is only logged.
///
/// These tests drive the real HTTP pipeline against real Postgres with the exact body the
/// Angular app sends, which no test did before: the other registration tests call the command
/// directly and always seed the User role first.
/// </summary>
[Collection("AuthPostgres")]
public sealed class RegisterWithoutSeededRolesTests
{
    private const string Password = "MyStrong!Pass93x";

    [Fact]
    public async Task RegisterWithTheBodyTheAppSends_Returns201_AndRecordsBothConsents()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", AppBody(email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Email == email);
        Assert.Equal(2, await db.ConsentRecords.CountAsync(x => x.UserId == user.Id));
        Assert.True(await UserHasRoleAsync(db, user.Id, RoleNames.User));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("privacy-declined")]
    [InlineData("terms-declined")]
    [InlineData("blank-version")]
    public async Task RegisterWithoutBothConsents_Returns422_AndCreatesNoAccount(string variant)
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        object body = variant switch
        {
            "missing" => new { firstName = "Naeem", lastName = "Test", email, password = Password },
            "privacy-declined" => new
            {
                firstName = "Naeem",
                lastName = "Test",
                email,
                password = Password,
                privacyPolicyAccepted = false,
                privacyPolicyVersion = "2026-10-01",
                termsAccepted = true,
                termsVersion = "2026-10-01"
            },
            "terms-declined" => new
            {
                firstName = "Naeem",
                lastName = "Test",
                email,
                password = Password,
                privacyPolicyAccepted = true,
                privacyPolicyVersion = "2026-10-01",
                termsAccepted = false,
                termsVersion = "2026-10-01"
            },
            _ => new
            {
                firstName = "Naeem",
                lastName = "Test",
                email,
                password = Password,
                privacyPolicyAccepted = true,
                privacyPolicyVersion = " ",
                termsAccepted = true,
                termsVersion = "2026-10-01"
            }
        };

        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AsNoTracking().AnyAsync(x => x.Email == email));
        Assert.Equal(0, await db.ConsentRecords.CountAsync());
    }

    [Fact]
    public async Task RegisterWhenTheRolesTableIsEmpty_Returns201_AndCreatesTheRole()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();
        await DeleteAllRolesAsync(factory);
        using var client = factory.CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", AppBody(email));

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Email == email);
        Assert.True(await UserHasRoleAsync(db, user.Id, RoleNames.User));
        Assert.Equal(1, await db.Roles.CountAsync(x => x.NormalizedName == "USER"));
    }

    [Fact]
    public async Task TwoFirstRegistrationsAtOnce_BothSucceed_AndCreateTheRoleOnce()
    {
        await using var factory = new PostgresAuthTestFactory();
        await factory.PrepareDatabaseAsync();
        await DeleteAllRolesAsync(factory);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ =>
                client.PostAsJsonAsync("/api/auth/register", AppBody(UniqueEmail()))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Roles.CountAsync(x => x.NormalizedName == "USER"));
        Assert.Equal(4, await db.UserRoles.CountAsync());
    }

    private static object AppBody(string email) => new
    {
        firstName = "Naeem",
        lastName = "Test",
        email,
        password = Password,
        privacyPolicyAccepted = true,
        privacyPolicyVersion = "2026-10-01",
        termsAccepted = true,
        termsVersion = "2026-10-01",
        consentSource = "Web"
    };

    private static string UniqueEmail() => $"register-roles-{Guid.NewGuid():N}@example.com";

    private static async Task DeleteAllRolesAsync(PostgresAuthTestFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserRoles.ExecuteDeleteAsync();
        await db.Roles.ExecuteDeleteAsync();
        Assert.Equal(0, await db.Roles.CountAsync());
    }

    private static async Task<bool> UserHasRoleAsync(AppDbContext db, Guid userId, string role)
        => await (
            from userRole in db.UserRoles
            join r in db.Roles on userRole.RoleId equals r.Id
            where userRole.UserId == userId && r.Name == role
            select userRole).AnyAsync();
}

/// <summary>Same failure through the phone sign-up workflow, which assigns the role itself.</summary>
public sealed class PhoneRegisterWithoutSeededRolesTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private readonly PhoneAuthWebApplicationFactory _factory;

    public PhoneRegisterWithoutSeededRolesTests(PhoneAuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PhoneRegistrationWhenTheRolesTableIsEmpty_Succeeds_AndCreatesTheRole()
    {
        using var client = _factory.CreateClient();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserRoles.RemoveRange(db.UserRoles);
            db.Roles.RemoveRange(db.Roles);
            await db.SaveChangesAsync();
            Assert.Equal(0, await db.Roles.CountAsync());
        }

        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var challengeId = (await send.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("challengeId").GetGuid();

        var verify = await client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = "SecurePass9",
            firstName = "Naeem",
            lastName = "Test"
        });

        Assert.True(
            verify.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)verify.StatusCode}: {await verify.Content.ReadAsStringAsync()}");
        await using var check = _factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await checkDb.Roles.CountAsync(x => x.Name == RoleNames.User));
    }
}
