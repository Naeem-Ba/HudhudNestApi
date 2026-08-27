using PropertyApi.Security.Auth;

namespace PropertyApi.Security.Csrf;

public sealed class CookieCsrfOptions
{
    public const string SectionName = "CookieCsrf";

    /// <summary>
    /// RELEASE-BLOCKERS-AR.md B-18. Off by default here so a config section that forgets to
    /// set this fails closed (no CSRF enforcement attempted, rather than enforcement against
    /// the wrong cookie) instead of failing open; every real environment must set this
    /// explicitly in its own appsettings rather than relying on this default.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The cookie whose mere presence on an unsafe request means "the browser is asserting
    /// ambient authority, so CSRF applies" (see CookieCsrfProtectionMiddleware). This is
    /// RefreshTokenCookie.Name, not ASP.NET Identity's own authentication cookie -- this API
    /// is JWT-bearer for access tokens; the refresh token is the only credential that ever
    /// travels as a cookie (RELEASE-BLOCKERS-AR.md B-13), and it is scoped to /api/auth, which
    /// is exactly where the unsafe, cookie-driven requests CSRF needs to cover live. Configuring
    /// this to ".AspNetCore.Identity.Application" (a cookie this app never sets) would make
    /// CookieCsrfProtectionMiddleware's hasAuthCookie check permanently false and Enabled=true
    /// would silently protect nothing.
    /// </summary>
    public string AuthenticationCookieName { get; set; } = RefreshTokenCookie.Name;

    public bool ProtectAnonymousUnsafeEndpoints { get; set; } = false;
}
