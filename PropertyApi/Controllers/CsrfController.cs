using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/security")]
[Produces("application/json")]
public sealed class CsrfController : ControllerBase
{
    [HttpGet("csrf-token")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }
}
