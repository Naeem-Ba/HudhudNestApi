using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PropertyApi.Security.Csrf;
using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Staging incident (2026-09-29): POST /api/auth/email/add answered 403 CSRF_VALIDATION_FAILED.
///
/// The SPA fetches its CSRF token once, anonymously (no Bearer on that call). ASP.NET Core
/// antiforgery binds a request token to the authenticated user, and UseAuthentication runs
/// BEFORE CookieCsrfProtectionMiddleware, so an authenticated request is validated against a
/// token minted for nobody and is rejected as "meant for a different claims-based user". The
/// refresh/logout calls escaped this only because they are sent without a Bearer.
///
/// A Bearer-authenticated request is not CSRF-exposed in the first place -- a browser never
/// attaches Authorization by itself -- so the middleware must not demand a token for it. The
/// tests below also pin the ways that exemption must NOT be reachable.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Csrf")]
public sealed class CookieCsrfBearerAuthenticationTests
{
    [Fact(DisplayName = "Control: an anonymous-minted token validates for an anonymous request (proves the identity binding is the cause)")]
    public async Task AnonymousToken_ValidatesForAnonymousRequest()
    {
        using var harness = new Harness();
        var (token, cookie) = harness.MintAnonymousToken();

        var reachedHandler = await harness.SendUnsafeAsync(
            cookie, token, bearer: null, authenticated: false);

        Assert.True(reachedHandler);
    }

    [Fact(DisplayName = "A Bearer-authenticated unsafe request carrying an anonymous-minted token is not blocked by CSRF")]
    public async Task BearerAuthenticated_WithAnonymouslyMintedToken_IsNotBlocked()
    {
        using var harness = new Harness();
        var (token, cookie) = harness.MintAnonymousToken();

        var reachedHandler = await harness.SendUnsafeAsync(
            cookie, token, bearer: "a.valid.jwt", authenticated: true);

        Assert.True(reachedHandler);
    }

    [Fact(DisplayName = "A Bearer-authenticated unsafe request needs no CSRF token at all")]
    public async Task BearerAuthenticated_WithNoToken_IsNotBlocked()
    {
        using var harness = new Harness();

        var reachedHandler = await harness.SendUnsafeAsync(
            cookie: null, token: null, bearer: "a.valid.jwt", authenticated: true);

        Assert.True(reachedHandler);
    }

    [Fact(DisplayName = "Ambient refresh_token cookie with no Bearer and no token is still rejected")]
    public async Task CookieOnly_WithNoToken_IsStillRejected()
    {
        using var harness = new Harness();

        await Assert.ThrowsAsync<AntiforgeryValidationException>(
            () => harness.SendUnsafeAsync(
                cookie: null, token: null, bearer: null, authenticated: false));
    }

    [Fact(DisplayName = "A forged Authorization header that did not authenticate does not exempt the request")]
    public async Task ForgedBearerHeader_ThatDidNotAuthenticate_IsStillRejected()
    {
        using var harness = new Harness();

        // Authorization is present but JwtBearer rejected it, so the principal is anonymous.
        await Assert.ThrowsAsync<AntiforgeryValidationException>(
            () => harness.SendUnsafeAsync(
                cookie: null, token: null, bearer: "forged", authenticated: false));
    }

    [Fact(DisplayName = "An authenticated principal without a Bearer header (some other scheme) does not exempt the request")]
    public async Task AuthenticatedWithoutBearerHeader_IsStillRejected()
    {
        using var harness = new Harness();

        await Assert.ThrowsAsync<AntiforgeryValidationException>(
            () => harness.SendUnsafeAsync(
                cookie: null, token: null, bearer: null, authenticated: true));
    }

    private sealed class Harness : IDisposable
    {
        private const string AuthCookie = "refresh_token=session-value";

        private readonly ServiceProvider _services;

        public Harness()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CookieCsrf:Enabled"] = "true",
                    ["CookieCsrf:AuthenticationCookieName"] = "refresh_token",
                    ["CookieCsrf:ProtectAnonymousUnsafeEndpoints"] = "false"
                })
                .Build();

            _services = new ServiceCollection()
                .AddLogging()
                .AddDataProtection().Services
                .AddPropertyApiAntiforgery(configuration, new TestEnvironment())
                .BuildServiceProvider();
        }

        /// <summary>What the SPA's CsrfTokenService gets: minted with no user on the request.</summary>
        public (string RequestToken, string CookiePair) MintAnonymousToken()
        {
            var context = NewContext();
            var tokens = _services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);

            var setCookie = context.Response.Headers.SetCookie
                .Select(header => header!.Split(';', 2)[0])
                .Single(pair => pair.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));

            return (tokens.RequestToken!, setCookie);
        }

        /// <summary>Runs the real middleware; true when the request got through to the next delegate.</summary>
        public async Task<bool> SendUnsafeAsync(
            string? cookie,
            string? token,
            string? bearer,
            bool authenticated)
        {
            var context = NewContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Headers.Cookie = cookie is null ? AuthCookie : $"{AuthCookie}; {cookie}";

            if (token is not null)
                context.Request.Headers[CsrfExtensions.HeaderName] = token;

            if (bearer is not null)
                context.Request.Headers.Authorization = $"Bearer {bearer}";

            context.User = authenticated
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    authenticationType: "Bearer"))
                : new ClaimsPrincipal(new ClaimsIdentity());

            var reached = false;
            var middleware = new CookieCsrfProtectionMiddleware(
                _ =>
                {
                    reached = true;
                    return Task.CompletedTask;
                },
                _services.GetRequiredService<IAntiforgery>(),
                _services.GetRequiredService<IOptions<CookieCsrfOptions>>());

            await middleware.InvokeAsync(context);

            return reached;
        }

        private DefaultHttpContext NewContext()
            => new() { RequestServices = _services };

        public void Dispose() => _services.Dispose();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "PropertyApi.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
