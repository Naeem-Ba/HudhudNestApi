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

            // Production incident (2026-09-18): SameAsRequest was chosen to avoid a hard
            // InvalidOperationException in Testing/CI (see below) -- but "adapt to the
            // request's own scheme" turned out to be actively wrong in Production. Confirmed
            // live with a direct curl to https://wohnungen-api.onrender.com (bypassing the
            // frontend/Netlify entirely): the Set-Cookie response for this exact endpoint
            // lacked `Secure` even over a genuine HTTPS connection. Render sits behind
            // Cloudflare, and Request.IsHttps reflects only the immediate hop's scheme as this
            // app's ForwardedHeaders middleware resolves it -- which does not reliably end up
            // "https" here, for reasons not fully diagnosable from outside Render's
            // infrastructure. A `SameSite=None` cookie missing `Secure` is dropped by every
            // browser outright, so this cookie (and the antiforgery check it backs) has
            // silently never survived a real Production request. It was masked until now by
            // the third-party-cookie bug refresh_token itself had (see RefreshTokenCookie.cs):
            // CookieCsrfProtectionMiddleware only enforces this check when refresh_token is
            // already present, and refresh_token was never surviving either, so the check was
            // never actually reached in practice. Fixing the transport bug (Partitioned +
            // same-origin proxy) exposed this second, independent one.
            //
            // Forcing Always specifically in Production sidesteps the unreliable IsHttps
            // detection instead of trying to fix it: Production is provably always served over
            // real HTTPS (enforced at startup elsewhere in this codebase), so there is nothing
            // adaptive to lose. Testing/CI still needs SameAsRequest -- ASP.NET Core's
            // antiforgery system actively throws InvalidOperationException out of
            // GetAndStoreTokens/ValidateRequestAsync when SecurePolicy=Always and the current
            // request is plain HTTP (DefaultAntiforgery.CheckSSLConfig; TestServer has no TLS)
            // -- this is not a silently-ignored cookie attribute like Secure on an ordinary
            // cookie, it is a hard 500 on every request. Development/real Staging are left on
            // SameAsRequest too: local dev is documented to require a real HTTPS profile
            // already (RefreshTokenCookie.cs), so SameAsRequest already resolves to Secure=true
            // there in practice, and Staging is a deliberately separate, smaller-blast-radius
            // fix to make later (also excluded from the same-origin proxy fix for now -- see
            // the frontend's auth-api-base-url.ts).
            options.Cookie.SecurePolicy = environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;

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
