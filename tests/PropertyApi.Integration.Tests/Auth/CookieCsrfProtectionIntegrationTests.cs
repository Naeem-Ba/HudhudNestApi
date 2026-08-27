using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PropertyApi.Integration.Tests.TestInfrastructure;
using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Exercises the real HTTP pipeline (RELEASE-BLOCKERS-AR.md B-18): CookieCsrfProtectionMiddleware
/// wired into the actual app, with CookieCsrf:Enabled=true layered on top of the Testing
/// defaults (which otherwise leave it off so the rest of the integration suite is not required
/// to carry a CSRF token on every unsafe request).
///
/// POST /api/auth/refresh is the attack surface: it is [AllowAnonymous] and reads the
/// refresh_token cookie the browser attaches automatically, which is exactly the "ambient
/// authority + unsafe method" combination CSRF exists to protect. Cookies are managed by hand
/// (HandleCookies = false) rather than relying on HttpClient's cookie jar, so each scenario
/// controls precisely which cookies travel with which request.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Csrf")]
public sealed class CookieCsrfProtectionIntegrationTests : IAsyncLifetime
{
    private TestApplication _app = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _app = TestApplication.CreateTesting(new Dictionary<string, string?>
        {
            ["CookieCsrf:Enabled"] = "true",
            ["CookieCsrf:AuthenticationCookieName"] = "refresh_token",
            ["CookieCsrf:ProtectAnonymousUnsafeEndpoints"] = "false"
        });

        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "GET csrf-token is reachable without any cookie and returns a usable XSRF-TOKEN cookie")]
    public async Task GetCsrfToken_Succeeds_AndSetsXsrfCookie()
    {
        using var response = await _client.GetAsync("/api/security/csrf-token");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var xsrfCookie = ExtractCookie(response, "XSRF-TOKEN");
        Assert.False(string.IsNullOrEmpty(xsrfCookie), "Expected a non-empty XSRF-TOKEN cookie in the response.");
    }

    [Fact(DisplayName = "POST refresh with no refresh_token cookie is not blocked by CSRF (nothing ambient to protect)")]
    public async Task Refresh_WithNoAuthCookie_IsNotBlockedByCsrf()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");

        using var response = await _client.SendAsync(request);

        // Reaches the handler and is rejected on business grounds (no token supplied at all),
        // not on CSRF grounds -- proving the CSRF gate only engages when the ambient cookie is
        // actually present.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("CSRF_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "POST refresh with the refresh_token cookie but no X-XSRF-TOKEN header is rejected as CSRF")]
    public async Task Refresh_WithAuthCookie_ButNoCsrfHeader_IsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", "refresh_token=not-a-real-token");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("CSRF_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "POST refresh with the refresh_token cookie and an invalid X-XSRF-TOKEN header is rejected as CSRF")]
    public async Task Refresh_WithAuthCookie_AndInvalidCsrfHeader_IsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", "refresh_token=not-a-real-token; XSRF-TOKEN=whatever-the-attacker-guesses");
        request.Headers.Add("X-XSRF-TOKEN", "attacker-guessed-value");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("CSRF_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "A cross-site attacker cannot trigger refresh with only the ambient cookie -- no token, no header they can forge")]
    public async Task CrossSiteAttacker_CannotDriveRefresh_WithoutTheToken()
    {
        // A real cross-site attacker's page can make the victim's browser attach the
        // refresh_token cookie automatically (that is what SameSite=None on that cookie
        // means), but has no way to read XSRF-TOKEN's value for a different origin (the
        // Same-Origin Policy blocks reading another origin's response/cookies), so it can
        // never produce a correct X-XSRF-TOKEN header. This reproduces exactly that: the
        // ambient cookie travels, no CSRF header is attached.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", "refresh_token=victim-session-cookie-value");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "POST refresh with the refresh_token cookie and a correctly-fetched CSRF token passes the CSRF gate")]
    public async Task Refresh_WithAuthCookie_AndValidCsrfToken_PassesCsrfGate()
    {
        // Step 1: the documented flow -- GET csrf-token first, with the refresh_token cookie
        // already present (mirrors a real browser: the cookie is ambient on every request to
        // this origin, including this GET).
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/security/csrf-token");
        tokenRequest.Headers.Add("Cookie", "refresh_token=not-a-real-token");
        using var tokenResponse = await _client.SendAsync(tokenRequest);
        Assert.Equal(HttpStatusCode.NoContent, tokenResponse.StatusCode);

        var xsrfCookieValue = ExtractCookie(tokenResponse, "XSRF-TOKEN");
        Assert.False(string.IsNullOrEmpty(xsrfCookieValue));

        // The antiforgery system also sets its own internal bookkeeping cookie (a different
        // name, a different value -- the "cookie token" half of the pair ValidateRequestAsync
        // cross-checks against the header's "request token"). A real browser sends every
        // cookie the origin has set automatically; this test has to reproduce that by hand
        // since cookies are managed manually here (HandleCookies = false).
        var allSetCookiePairs = ExtractAllCookiePairs(tokenResponse);
        Assert.True(
            allSetCookiePairs.Count >= 2,
            "Expected both the visible XSRF-TOKEN cookie and the antiforgery system's own " +
            "internal cookie to be set by GET csrf-token.");

        // Step 2: echo the visible cookie's value back as the X-XSRF-TOKEN header, exactly as
        // Angular's HttpClientXsrfModule (or any client following the same convention) does,
        // while resending every cookie the origin set (plus the ambient refresh_token) as a
        // real browser would.
        var cookieHeader = "refresh_token=not-a-real-token; " +
            string.Join("; ", allSetCookiePairs.Select(pair => $"{pair.Key}={pair.Value}"));

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", cookieHeader);
        refreshRequest.Headers.Add("X-XSRF-TOKEN", xsrfCookieValue);

        using var refreshResponse = await _client.SendAsync(refreshRequest);

        // Must NOT be the CSRF rejection -- the request has to reach the handler, which then
        // rejects it on business grounds (not-a-real-token is not a valid refresh token).
        var body = await refreshResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("CSRF_VALIDATION_FAILED", body);
        Assert.NotEqual(HttpStatusCode.Forbidden, refreshResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    private static string? ExtractCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            return null;

        foreach (var header in setCookieHeaders)
        {
            var prefix = cookieName + "=";
            if (!header.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var valueAndAttributes = header[prefix.Length..];
            var semicolonIndex = valueAndAttributes.IndexOf(';');
            return semicolonIndex >= 0
                ? valueAndAttributes[..semicolonIndex]
                : valueAndAttributes;
        }

        return null;
    }

    /// <summary>Every cookie name/value pair the response set, ignoring attributes (Path, Secure, ...).</summary>
    private static IReadOnlyList<KeyValuePair<string, string>> ExtractAllCookiePairs(HttpResponseMessage response)
    {
        var pairs = new List<KeyValuePair<string, string>>();

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            return pairs;

        foreach (var header in setCookieHeaders)
        {
            var nameValue = header.Split(';', 2)[0];
            var equalsIndex = nameValue.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            pairs.Add(new KeyValuePair<string, string>(
                nameValue[..equalsIndex],
                nameValue[(equalsIndex + 1)..]));
        }

        return pairs;
    }
}
