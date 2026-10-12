using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Auth;

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

/// <summary>
/// Logout with CookieCsrf enforced, using the CSRF token fetched *before* sign-in -- exactly what the
/// SPA and the Staging smoke do. Antiforgery request tokens are bound to the authenticated user, so
/// this used to be rejected with 403 CSRF_VALIDATION_FAILED (the refresh token was never revoked).
/// A Bearer-authenticated logout is exempt (a cross-site page cannot attach the header); a logout
/// that carries only the ambient refresh cookie -- the access token expired -- must present the CSRF
/// header exactly like refresh, and then still works instead of answering 401 and keeping the session.
/// </summary>
public sealed class LogoutWithCsrfIntegrationTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private readonly PhoneAuthWebApplicationFactory _factory;

    public LogoutWithCsrfIntegrationTests(PhoneAuthWebApplicationFactory factory) => _factory = factory;

    private sealed record Session(HttpClient Client, List<string> CookiePairs, string Csrf, string Access, string RefreshCookie)
    {
        public async Task<HttpResponseMessage> Post(string path, string? bearer, bool withCsrfHeader)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("Cookie", string.Join("; ", CookiePairs.Append(RefreshCookie)));
            if (withCsrfHeader) request.Headers.Add("X-XSRF-TOKEN", Csrf);
            if (bearer is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            request.Content = JsonContent.Create(new { RefreshToken = RefreshCookie["refresh_token=".Length..] });
            return await Client.SendAsync(request);
        }
    }

    private async Task<Session> SignIn()
    {
        var app = _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CookieCsrf:Enabled"] = "true",
                ["CookieCsrf:AuthenticationCookieName"] = "refresh_token",
                ["CookieCsrf:ProtectAnonymousUnsafeEndpoints"] = "false"
            })));
        var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        using var tokenResponse = await client.GetAsync("/api/security/csrf-token");
        var cookiePairs = tokenResponse.Headers.GetValues("Set-Cookie").Select(h => h.Split(';', 2)[0]).ToList();
        var csrf = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("csrfToken").GetString()!;

        var phone = $"+49{Math.Abs(DateTime.UtcNow.Ticks % 10_000_000_000_000L):D13}";
        var send = await client.PostAsJsonAsync("/api/auth/phone/registration/send-otp", new { phoneNumber = phone });
        var challengeId = JsonDocument.Parse(await send.Content.ReadAsStringAsync()).RootElement.GetProperty("challengeId").GetGuid();
        var registration = await client.PostAsJsonAsync("/api/auth/phone/registration/verify", new
        {
            challengeId,
            code = DeterministicOtpService.ValidOtp,
            password = "SecurePass9",
            firstName = "N",
            lastName = "B"
        });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/phone/login", new { phoneNumber = phone, password = "SecurePass9" });
        var access = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
        var refreshCookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("refresh_token=", StringComparison.Ordinal)).Split(';')[0];
        return new Session(client, cookiePairs, csrf, access, refreshCookie);
    }

    [Fact(DisplayName = "Logout with CSRF enforced succeeds with a pre-sign-in token and refresh stays CSRF-protected")]
    [Trait("Category", "Security")]
    public async Task Logout_WithCsrfEnforced_IsNotBlocked_AndRefreshStaysProtected()
    {
        var session = await SignIn();

        // Refresh (anonymous + ambient cookie) is still rejected without the CSRF header.
        var refreshWithoutCsrf = await session.Post("/api/auth/refresh", bearer: null, withCsrfHeader: false);
        Assert.Equal(HttpStatusCode.Forbidden, refreshWithoutCsrf.StatusCode);
        Assert.Contains("CSRF_VALIDATION_FAILED", await refreshWithoutCsrf.Content.ReadAsStringAsync());

        // Logout with Bearer and the pre-sign-in CSRF token succeeds and revokes the session.
        var logout = await session.Post("/api/auth/logout", session.Access, withCsrfHeader: true);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact(DisplayName = "Logout with an expired access token still revokes the refresh token, and a cross-site cookie-only logout is refused")]
    [Trait("Category", "Security")]
    public async Task Logout_WithExpiredAccessToken_RevokesTheSession_ButNeedsTheCsrfHeader()
    {
        var session = await SignIn();

        // Cookie only, no Bearer, no CSRF header: what a cross-site form post looks like. Refused, session intact.
        var crossSite = await session.Post("/api/auth/logout", bearer: null, withCsrfHeader: false);
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
        Assert.Contains("CSRF_VALIDATION_FAILED", await crossSite.Content.ReadAsStringAsync());

        // The SPA's case: the 30-minute access token is dead, the cookie and the CSRF header are present.
        var expired = await session.Post("/api/auth/logout", "expired.access.token", withCsrfHeader: true);
        Assert.Equal(HttpStatusCode.NoContent, expired.StatusCode);
        Assert.Contains(expired.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("refresh_token=", StringComparison.Ordinal) && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        // The refresh token is really gone: it cannot silently restore the session.
        var refresh = await session.Post("/api/auth/refresh", bearer: null, withCsrfHeader: true);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
