using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// Core E2E verification 2026-10-03: a refresh_token cookie the server can no longer honour (expired,
/// revoked, unknown) used to survive the 401 from POST /api/auth/refresh. While it lingered,
/// CookieCsrfProtectionMiddleware treated every later unsafe request -- including the anonymous
/// phone login -- as cookie-authenticated and demanded an X-XSRF-TOKEN the SPA had just thrown away
/// after the failed refresh, so the user got 403 CSRF_VALIDATION_FAILED on every login attempt until
/// the cookie expired on its own.
/// </summary>
public sealed class RefreshDeadCookieIntegrationTests : IClassFixture<PhoneAuthWebApplicationFactory>
{
    private readonly PhoneAuthWebApplicationFactory _factory;

    public RefreshDeadCookieIntegrationTests(PhoneAuthWebApplicationFactory factory) => _factory = factory;

    [Fact(DisplayName = "A refresh that fails clears the dead refresh_token cookie so a later anonymous login is not CSRF-blocked")]
    [Trait("Category", "Security")]
    public async Task FailedRefresh_ClearsTheDeadCookie_SoAnonymousLoginStillWorks()
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
        var csrf = System.Text.Json.JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("csrfToken").GetString()!;

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("Cookie", string.Join("; ", cookiePairs.Append("refresh_token=revoked-or-unknown")));
        refresh.Headers.Add("X-XSRF-TOKEN", csrf);
        refresh.Content = JsonContent.Create(new { });
        using var refreshResponse = await client.SendAsync(refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
        var setCookies = refreshResponse.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : new List<string>();
        var cleared = setCookies.SingleOrDefault(c => c.StartsWith("refresh_token=;", StringComparison.Ordinal));
        Assert.NotNull(cleared);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared!, StringComparison.OrdinalIgnoreCase);
    }
}
