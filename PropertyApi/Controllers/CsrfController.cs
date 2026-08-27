using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Security.Csrf;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/security")]
[Produces("application/json")]
public sealed class CsrfController : ControllerBase
{
    /// <summary>
    /// RELEASE-BLOCKERS-AR.md B-18. <c>IAntiforgery.GetAndStoreTokens</c> returns two
    /// different token values -- a cookie token (which it already writes itself, as a
    /// side effect, into its own internal HttpOnly cookie) and a request token (meant to
    /// travel back via header/form on the next unsafe request). Only explicitly writing
    /// <c>tokens.RequestToken</c> into a *second*, JS-readable cookie here gives the SPA
    /// anything correct to echo back as X-XSRF-TOKEN -- simply reading the framework's own
    /// (previously non-HttpOnly) cookie and resending its value does not validate, because
    /// that cookie never held the request token in the first place. This mirrors Microsoft's
    /// documented "Angular convention" for ASP.NET Core antiforgery.
    /// </summary>
    [HttpGet("csrf-token")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        Response.Cookies.Append(
            CsrfExtensions.VisibleTokenCookieName,
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,

                // Same reasoning as the internal antiforgery cookie in CsrfExtensions: must
                // match RefreshTokenCookie's cross-site posture, and SecurePolicy has to
                // adapt to the request's own scheme rather than being hard-coded true, or
                // this cookie silently would not be set at all on plain-HTTP Testing/CI
                // (browsers drop SameSite=None cookies missing Secure outright -- unlike the
                // antiforgery-internal cookie above, a plain CookieOptions.Secure=true on
                // HTTP does not throw here, it just produces a cookie no HTTP client can use).
                SameSite = SameSiteMode.None,
                Secure = Request.IsHttps
            });

        return NoContent();
    }
}
