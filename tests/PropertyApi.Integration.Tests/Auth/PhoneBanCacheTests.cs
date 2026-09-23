using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// A ban is enforced per request through the cached security-stamp snapshot (5-minute TTL). Once a token has
/// been used, the snapshot is cached, so a ban written straight to the database only takes effect when the
/// TTL ends UNLESS the ban workflow invalidates the cache. This documents that behaviour with a real host so
/// whoever builds the ban workflow knows the one call it must make (IUserSecurityStampCacheInvalidator).
/// </summary>
public sealed class PhoneBanCacheTests : IClassFixture<PhoneLoginAuditFactory>, IAsyncLifetime
{
    private const string Password = "SecurePass9";
    private static int _seed = Environment.TickCount & 0xFFFFFF;
    private readonly PhoneLoginAuditFactory _factory;

    public PhoneBanCacheTests(PhoneLoginAuditFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewPhone() => $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";

    private async Task<(string Phone, Guid UserId, string Access)> Register()
    {
        var phone = NewPhone();
        using var client = _factory.CreateClient(new() { HandleCookies = false });
        var send = await client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await send.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        var verify = await client.PostAsJsonAsync("/api/auth/phone/registration/verify",
            new { challengeId, code = _factory.Sms.LastCodeFor(phone), password = Password, firstName = "Ban", lastName = "Cache" });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var access = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Users.Where(u => u.NormalizedPhoneNumber == phone).Select(u => u.Id).SingleAsync();
        return (phone, id, access);
    }

    private async Task<HttpStatusCode> CallWithToken(string access)
    {
        using var client = _factory.CreateClient(new() { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/phone/reverification/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return (await client.SendAsync(request)).StatusCode;
    }

    private async Task Ban(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.IsBanned = true;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ban_WithoutCacheInvalidation_TakesEffectOnlyAfterTheCacheTtl_AndInvalidationMakesItImmediate()
    {
        var (_, userId, access) = await Register();
        Assert.Equal(HttpStatusCode.OK, await CallWithToken(access)); // populates the cached snapshot

        await Ban(userId);
        Assert.Equal(HttpStatusCode.OK, await CallWithToken(access)); // known residual: still cached

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserSecurityStampCacheInvalidator>().InvalidateAsync(userId);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithToken(access)); // immediate once invalidated
    }
}
