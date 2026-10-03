using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Security audit 2026-10-03, F-10 (and the admin half of F-03). Kept in its own class because each
/// class gets its own host, and the send-otp limiter allows only three registrations per host.
/// </summary>
public sealed class AdminDisableUserSecurityTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9";

    private readonly PhoneAuthWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminDisableUserSecurityTests(PhoneAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- F-10 ------------------------------------------------------------------------------

    [Fact(DisplayName = "F-10: an admin cannot disable their own account")]
    [Trait("Category", "Security")]
    public async Task Admin_CannotDisableThemselves()
    {
        var admin = await RegisterAsync(makeAdmin: true);

        var response = await Send(HttpMethod.Delete, $"/api/admin/users/{admin.Id}", admin.Access);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/users/me", admin.Access)).StatusCode);
    }

    [Fact(DisplayName = "F-10/F-03: disabling a user is audited and ends their session at once")]
    [Trait("Category", "Security")]
    public async Task DisableUser_IsAudited_AndEndsTheTargetsSessionImmediately()
    {
        var admin = await RegisterAsync(makeAdmin: true);
        var target = await RegisterAsync();

        // Prime the stamp cache with a request that succeeds before the account is disabled.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/users/me", target.Access)).StatusCode);

        var response = await Send(HttpMethod.Delete, $"/api/admin/users/{target.Id}", admin.Access);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/users/me", target.Access)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogs.SingleAsync(x => x.Action == AuditActions.UserDisabled);
        Assert.Equal(admin.Id, entry.UserId);
        Assert.Contains(target.Id.ToString(), entry.NewValue);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private async Task<(Guid Id, string Access)> RegisterAsync(bool makeAdmin = false)
    {
        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        await Task.Delay(2); // ticks must differ between users registered back to back

        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await Json(send)).GetProperty("challengeId").GetGuid();
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Audit",
            lastName = "Subject"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        if (makeAdmin)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.Users.SingleAsync(x => x.NormalizedPhoneNumber == phone);
            Assert.True((await users.AddToRoleAsync(user, RoleNames.Admin)).Succeeded);
        }

        // Log in after the role change so the access token carries the role claim.
        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var access = (await Json(login)).GetProperty("accessToken").GetString()!;

        var me = await Json(await Send(HttpMethod.Get, "/api/users/me", access));
        return (me.GetProperty("id").GetGuid(), access);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

}
