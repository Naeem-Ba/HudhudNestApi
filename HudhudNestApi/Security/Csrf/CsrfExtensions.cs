using Microsoft.AspNetCore.Http;

namespace HudhudNestApi.Security.Csrf;

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

    public static IServiceCollection AddHudhudNestApiAntiforgery(
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

            // Production incident (2026-09-18): confirmed live with a direct curl to
            // https://wohnungen-api.onrender.com (bypassing the frontend/Netlify entirely)
            // that this cookie's Set-Cookie lacked `Secure` even over a genuine HTTPS
            // connection -- Request.IsHttps was false. A `SameSite=None` cookie missing
            // `Secure` is dropped by every browser outright, so this cookie (and the
            // antiforgery check it backs) had silently never survived a real Production
            // request. It was masked until now by the third-party-cookie bug refresh_token
            // itself had (see RefreshTokenCookie.cs): CookieCsrfProtectionMiddleware only
            // enforces this check when refresh_token is already present, and refresh_token
            // was never surviving either, so the check was never actually reached in practice.
            //
            // A first attempt forced SecurePolicy=Always specifically in Production instead of
            // fixing Request.IsHttps -- that made every request to this endpoint 500 in
            // Production within minutes of deploying: SecurePolicy=Always makes
            // DefaultAntiforgery.CheckSSLConfig throw InvalidOperationException whenever
            // Request.IsHttps is false, and it still was, so this made things strictly worse.
            // The actual, working fix is Program.cs forcing Request.Scheme = "https" in
            // Production right after UseForwardedHeaders() -- see that file's own comment for
            // why Request.IsHttps cannot be trusted there. With that in place, plain
            // SameAsRequest (the original, pre-incident value) now correctly resolves to
            // Secure=true in a real Production request the same way it always should have,
            // with no environment-specific branch needed here at all.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            // See RefreshTokenCookie.cs's doc comment ("Production incident 2026-09"): this
            // cookie has the exact same cross-site posture as refresh_token, so it needs the
            // same CHIPS fix or the antiforgery check it backs would fail even once
            // refresh_token itself starts surviving.
            options.Cookie.Extensions.Add("Partitioned");
        });

        return services;
    }

    public static IApplicationBuilder UseCookieCsrfProtection(this IApplicationBuilder app)
        => app.UseMiddleware<CookieCsrfProtectionMiddleware>();
}
