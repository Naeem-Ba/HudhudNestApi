using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Security;

/// <summary>
/// Production report 2026-10-08: after "send activation link" in account settings, GET /api/users/me
/// answered 401 invalid_token for every token, and logging out and back in did not help.
///
/// UserManager.SetEmailAsync rotates the security stamp. CachedSecurityStampValidator keeps a
/// 5-minute snapshot of the stamp; unless it is dropped, the snapshot still holds the OLD stamp, so a
/// token issued afterwards (it carries the NEW one) fails the comparison until the entry expires.
/// Its own class so that it has its own host and its own send-otp rate-limit budget.
/// </summary>
public sealed class EmailAddStampCacheTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private const string Password = "SecurePass9";

    private readonly HttpClient _client;

    public EmailAddStampCacheTests(PhoneAuthWebApplicationFactory factory)
        => _client = factory.CreateClient();

    [Fact(DisplayName = "Adding an email does not leave the stamp cache stale: the next login works straight away")]
    [Trait("Category", "Security")]
    public async Task AddEmail_NextLogin_IsNotRejectedByAStaleStampCache()
    {
        var (phone, access) = await RegisterAndLoginAsync();

        // Prime the stamp cache the way any ordinary request would.
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(access)).StatusCode);

        var add = await SendAsync(
            HttpMethod.Post,
            "/api/auth/email/add",
            access,
            new { email = $"stamp-{Guid.NewGuid():N}@example.com", language = "ar" });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var freshAccess = (await Json(login)).GetProperty("accessToken").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(freshAccess)).StatusCode);
    }

    private async Task<(string Phone, string Access)> RegisterAndLoginAsync()
    {
        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await Json(send)).GetProperty("challengeId").GetGuid();
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = Password,
            firstName = "Stamp",
            lastName = "Cache"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (phone, (await Json(login)).GetProperty("accessToken").GetString()!);
    }

    private Task<HttpResponseMessage> GetMeAsync(string access) => SendAsync(HttpMethod.Get, "/api/users/me", access);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
