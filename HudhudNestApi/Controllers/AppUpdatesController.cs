using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.AppUpdates.Queries.CheckAppUpdate;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Public app-update check — works identically for authenticated and anonymous callers, and
/// exposes nothing sensitive. See docs/app-update-management.md for the mandatory-vs-optional
/// decision rule.
/// </summary>
[ApiController]
[Route("api/app-updates")]
public sealed class AppUpdatesController : ControllerBase
{
    private readonly ISender _sender;

    public AppUpdatesController(ISender sender) => _sender = sender;

    [HttpGet("check")]
    [AllowAnonymous] // Public, read-only, no sensitive data — see PublicEndpointPolicyTests.
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Check(
        [FromQuery] AppPlatform platform,
        [FromQuery] string currentVersion,
        [FromQuery] string? lang,
        CancellationToken ct)
        => Ok(await _sender.Send(new CheckAppUpdateQuery(platform, currentVersion, lang), ct));
}
