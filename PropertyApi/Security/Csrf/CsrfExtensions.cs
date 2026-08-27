using Microsoft.AspNetCore.Http;

namespace PropertyApi.Security.Csrf;

public static class CsrfExtensions
{
    /// <summary>
    /// The cookie CsrfController.GetCsrfToken writes explicitly, carrying the antiforgery
    /// *request* token (see that controller's doc comment for why this has to be a separate
    /// cookie from the framework's own internal one). This is the cookie the SPA reads and
    /// echoes back as the <see cref="HeaderName"/> header -- Angular's HttpClientXsrfModule
    /// does exactly this by default for a cookie of this name.
    /// </summary>
    public const string VisibleTokenCookieName = "XSRF-TOKEN";

    public const string HeaderName = "X-XSRF-TOKEN";

    public static IServiceCollection AddPropertyApiAntiforgery(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<CookieCsrfOptions>()
            .Bind(configuration.GetSection(CookieCsrfOptions.SectionName))
            .Validate(
                options =>
                    !options.Enabled ||
                    !string.IsNullOrWhiteSpace(options.AuthenticationCookieName),
                "CookieCsrf:AuthenticationCookieName is required when CookieCsrf:Enabled=true.")
            .ValidateOnStart();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;

            // Deliberately NOT named VisibleTokenCookieName ("XSRF-TOKEN") and NOT
            // HttpOnly=false. This is the framework's own bookkeeping cookie -- it carries
            // the antiforgery *cookie* token, a different value from the *request* token the
            // header carries, and ValidateRequestAsync cryptographically binds the two
            // together. It stays at its ASP.NET Core default name (unique per app, avoids
            // colliding with VisibleTokenCookieName below) and its default HttpOnly=true --
            // nothing in JS ever needs to read it, only send it back automatically like any
            // other cookie. Naming both cookies "XSRF-TOKEN" (the previous version of this
            // file did) makes CsrfController.GetCsrfToken's explicit
            // Response.Cookies.Append(VisibleTokenCookieName, tokens.RequestToken, ...)
            // silently overwrite this one, so ValidateRequestAsync ends up reading a
            // request-token-shaped blob where it expects a cookie-token-shaped one on the
            // next request and fails every legitimate call.

            // Must match RefreshTokenCookie's SameSite=None, not be stricter than it. The
            // frontend and this API are different origins (RefreshTokenCookie's own comment),
            // so both this cookie and VisibleTokenCookieName travel cross-site on every real
            // deployment. SameSite=Strict (the previous value here) is dropped by the browser
            // on a cross-site request, so ValidateRequestAsync could never see this cookie at
            // all once CookieCsrf:Enabled=true -- every protected unsafe request would fail
            // unconditionally, indistinguishable from an actual missing/invalid token.
            options.Cookie.SameSite = SameSiteMode.None;

            // SameAsRequest, not the seemingly-stronger Always: ASP.NET Core's antiforgery
            // system actively throws InvalidOperationException out of GetAndStoreTokens /
            // ValidateRequestAsync when SecurePolicy=Always and the current request is plain
            // HTTP (Microsoft.AspNetCore.Antiforgery.DefaultAntiforgery.CheckSSLConfig) --
            // this is not a silently-ignored cookie attribute like Secure on an ordinary
            // cookie, it is a hard 500 on every request. Testing/CI run the app over plain
            // HTTP (TestServer has no TLS), so Always made GET /api/security/csrf-token and
            // every CSRF-protected endpoint fail outright the moment CookieCsrf:Enabled=true
            // in that environment. SameAsRequest adapts instead of enforcing: Secure=true on
            // the real HTTPS traffic Production and a correctly-configured local HTTPS
            // profile actually serve (still satisfying SameSite=None's browser requirement
            // there), Secure=false on plain-HTTP Testing/CI, and never throws either way.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }

    public static IApplicationBuilder UseCookieCsrfProtection(this IApplicationBuilder app)
        => app.UseMiddleware<CookieCsrfProtectionMiddleware>();
}
