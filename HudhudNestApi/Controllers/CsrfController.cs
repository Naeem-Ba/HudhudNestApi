using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Security.Csrf;

namespace HudhudNestApi.Controllers;

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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        Response.Cookies.Append(
            CsrfExtensions.VisibleTokenCookieName,
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,

                // Production incident (2026-09-18): Request.IsHttps is NOT a reliable signal
                // in Production -- see CsrfExtensions.cs's own doc comment on the internal
                // antiforgery cookie for the full story (confirmed live via a direct curl to
                // the backend: Secure was missing from this exact cookie even over genuine
                // HTTPS). Unlike that framework-managed cookie, a plain CookieOptions on an
                // ordinary Response.Cookies.Append never throws regardless of the current
                // request's actual scheme -- Secure is just a flag written into the header,
                // not a runtime HTTPS-enforcement check -- so there is no Testing/CI exception
                // to preserve here at all; hardcoding true is strictly safe, matching
                // RefreshTokenCookie.Attach's own already-hardcoded Secure=true.
                SameSite = SameSiteMode.None,
                Secure = true,
                Extensions = { "Partitioned" }
            });

        // RELEASE-BLOCKERS-AR.md B-20: the XSRF-TOKEN cookie above is host-only and, in
        // deployed environments, the SPA and this API sit on hostnames that share no
        // registrable domain (e.g. realestateworld.world vs. onrender.com) -- so
        // document.cookie on the SPA's origin can never see it there, no matter what Domain
        // is (or isn't) set on the cookie. Returning the same request token in the response
        // body lets the client read it directly instead of depending on cross-site cookie
        // visibility, while the cookie itself is kept for any consumer that can rely on it
        // (e.g. same-host local development).
        return Ok(new { csrfToken = tokens.RequestToken });
    }
}
