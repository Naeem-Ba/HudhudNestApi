namespace HudhudNestApi.Security.Auth;

/// <summary>
/// Single source of truth for the refresh-token cookie's name and attributes
/// (RELEASE-BLOCKERS-AR.md B-13).
///
/// Before this, the refresh token travelled only in the JSON response body and the Angular
/// client persisted it to sessionStorage — readable by any script that ever runs on the
/// page, including an XSS payload. Every login surface (email/password, Google, Apple,
/// phone/password) now calls Attach() on success instead of/alongside returning the token
/// in the body; the one POST /api/auth/refresh and POST /api/auth/logout call Read()/Clear().
///
/// SameSite=None + Secure is mandatory for a cookie sent cross-origin (the Angular app and
/// this API are different origins/ports even in local development) — browsers refuse to
/// honour SameSite=None without Secure. That means the API must be served over HTTPS
/// wherever this cookie needs to work, including local development (use the "https" launch
/// profile, not "http"). Path is scoped to /api/auth: nothing outside the two endpoints that
/// read this cookie has any reason to receive it on every request.
///
/// Production incident (2026-09): on the real deployed frontend (realestateworld.world) and
/// backend (onrender.com — a different registrable domain, per this file's own comment
/// above), a plain SameSite=None cookie is a third-party cookie by definition, and Chrome/Edge
/// were confirmed (live, via POST /api/auth/refresh returning "No refresh token was supplied"
/// immediately after a real login) to be dropping it outright — every page reload looked
/// exactly like a first-time guest, 100% of the time, not intermittently. This is a browser
/// third-party-cookie policy, not a timing/race bug (see app.config.ts's restoreSession fix,
/// which addressed a real but different, secondary issue and did not touch this one). The
/// `Partitioned` extension (CHIPS) is Chrome/Edge's sanctioned fix for exactly this pattern —
/// one frontend consistently talking to one third-party API domain: the browser stores the
/// cookie scoped to (this cookie's domain, the top-level site), so it is no longer treated as
/// a blockable cross-site tracking cookie, and correctly persists across reloads. Harmless
/// where unsupported (Safari, older browsers) — an unrecognized cookie attribute is ignored,
/// leaving today's (broken, for those browsers) behavior unchanged, not worse.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "refresh_token";

    private const string Path = "/api/auth";

    public static void Attach(HttpResponse response, string refreshToken, int refreshTokenDays)
    {
        response.Cookies.Append(Name, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = Path,
            Expires = DateTimeOffset.UtcNow.AddDays(refreshTokenDays),
            Extensions = { "Partitioned" }
        });
    }

    /// <summary>
    /// Reads the token an anonymous caller is asserting. Prefers an explicit body value over
    /// the ambient cookie: this is what lets a still-migrating client (or an internal test
    /// tool asserting a specific, deliberately stale token) keep working during the
    /// transition, while a real browser client — which after this change never has a
    /// non-empty body value to send — always resolves to the cookie exclusively.
    /// </summary>
    public static string? Read(HttpRequest request, string? bodyValue)
    {
        if (!string.IsNullOrWhiteSpace(bodyValue))
            return bodyValue;

        return request.Cookies.TryGetValue(Name, out var cookieValue) && !string.IsNullOrWhiteSpace(cookieValue)
            ? cookieValue
            : null;
    }

    public static void Clear(HttpResponse response)
    {
        // Attributes must match Attach()'s: a browser matches a Set-Cookie deletion against
        // the exact cookie it stored (including its Partitioned jar), not just by name/path.
        response.Cookies.Delete(Name, new CookieOptions
        {
            Path = Path,
            Secure = true,
            SameSite = SameSiteMode.None,
            Extensions = { "Partitioned" }
        });
    }
}
