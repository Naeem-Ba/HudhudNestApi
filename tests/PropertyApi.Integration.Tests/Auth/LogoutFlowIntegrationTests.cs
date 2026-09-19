using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PropertyApi.Integration.Tests.TestInfrastructure;

namespace PropertyApi.Integration.Tests.Auth;

public sealed class LogoutFlowIntegrationTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LogoutFlowIntegrationTests(PhoneAuthWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Logout rotates the Identity security stamp and invalidates the cached stamp
    /// (LogoutCommandHandler), and the JWT bearer pipeline validates the stamp claim on every request,
    /// so an explicit logout also ends the current access token -- it is not merely a refresh-token
    /// revocation. Staging smoke previously asserted the opposite ("stateless access token stays
    /// valid"). Replaying the revoked refresh token is then rejected as reuse.
    /// </summary>
    [Fact(DisplayName = "Logout answers 204, ends the access token and the revoked refresh token cannot be replayed")]
    [Trait("Category", "Integration")]
    public async Task Logout_RevokesRefreshToken_AndEndsTheAccessToken()
    {
        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await _client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = (await Json(send)).GetProperty("challengeId").GetGuid();
        var registration = await _client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = "SecurePass9",
            firstName = "Naeem",
            lastName = "Bazzazeh"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = "SecurePass9" });
        var access = (await Json(login)).GetProperty("accessToken").GetString()!;
        var refresh = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("refresh_token=", StringComparison.Ordinal))
            .Split(';')[0]["refresh_token=".Length..];

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/users/me", access)).StatusCode);

        var logout = await Send(HttpMethod.Post, "/api/auth/logout", access, new { RefreshToken = refresh });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/users/me", access)).StatusCode);

        var replay = await _client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
