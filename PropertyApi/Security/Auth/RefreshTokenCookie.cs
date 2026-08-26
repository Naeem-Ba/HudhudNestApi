namespace PropertyApi.Security.Auth;

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
            Expires = DateTimeOffset.UtcNow.AddDays(refreshTokenDays)
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
        response.Cookies.Delete(Name, new CookieOptions { Path = Path });
    }
}
