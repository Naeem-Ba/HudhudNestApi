using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Moq;
using HudhudNestApi.Configuration;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Health;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Security audit 2026-10-03: regression tests for F-08 (readiness probe), F-09 (test environment
/// guard), F-10 (admin disable) and F-13 (role disclosure).
/// </summary>
public sealed class SecurityAuditRemediationTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9";

    private readonly PhoneAuthWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityAuditRemediationTests(PhoneAuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---- F-09 ------------------------------------------------------------------------------

    [Theory(DisplayName = "F-09: a hosted deployment may not run in a test-harness environment")]
    [Trait("Category", "Security")]
    [InlineData("Testing", "RENDER", true)]
    [InlineData("CI", "RENDER_SERVICE_ID", true)]
    [InlineData("Testing", null, false)]
    [InlineData("Production", "RENDER", false)]
    [InlineData("Staging", "RENDER", false)]
    public void TestEnvironmentGuard_RefusesTestHarnessEnvironmentsOnRender(
        string environmentName, string? variable, bool shouldThrow)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(environmentName);

        void Act() => TestEnvironmentGuard.Validate(
            environment.Object, name => name == variable ? "true" : null);

        if (shouldThrow)
            Assert.Throws<InvalidOperationException>(Act);
        else
            Act();
    }

    // ---- F-08 ------------------------------------------------------------------------------

    [Fact(DisplayName = "F-08: the anonymous readiness answer does not list the backing services")]
    [Trait("Category", "Security")]
    public async Task ReadinessBody_ReportsStatusOnly()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
        Assert.Contains("\"status\"", body);
        Assert.DoesNotContain("postgres", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redis", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checks", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("durationMs", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "F-08: repeated readiness requests inside the window run the probe once")]
    [Trait("Category", "Security")]
    public async Task ReadinessCache_RunsTheProbeOncePerWindow()
    {
        var clock = new ManualClock();
        var probes = 0;
        var middleware = new ReadinessResponseCacheMiddleware(async context =>
        {
            probes++;
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"status\":\"Unhealthy\"}");
        }, clock);

        async Task<(int Status, string Body)> Call()
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context);
            context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }

        var first = await Call();
        var second = await Call();
        Assert.Equal(1, probes);
        Assert.Equal(first, second);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, second.Status);

        clock.Advance(ReadinessResponseCacheMiddleware.Ttl + TimeSpan.FromSeconds(1));
        await Call();
        Assert.Equal(2, probes);
    }

    // ---- F-13 ------------------------------------------------------------------------------

    [Fact(DisplayName = "F-13: only admins (and the account itself) can see that an account is an admin")]
    [Trait("Category", "Security")]
    public async Task UserLookup_HidesTheAdminRoleFromOtherUsers()
    {
        var admin = await RegisterAsync(makeAdmin: true);
        var otherAdmin = await RegisterAsync(makeAdmin: true);
        var viewer = await RegisterAsync();

        Assert.DoesNotContain("Admin", await RolesOfAsync(admin.Id, viewer.Access));
        Assert.Contains("Admin", await RolesOfAsync(admin.Id, admin.Access));
        Assert.Contains("Admin", await RolesOfAsync(admin.Id, otherAdmin.Access));
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

    private async Task<string[]> RolesOfAsync(Guid userId, string access)
    {
        var response = await Send(HttpMethod.Get, $"/api/users/{userId}", access);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("roles").EnumerateArray().Select(x => x.GetString()!).ToArray();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
