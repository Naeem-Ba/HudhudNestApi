using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Plans.Queries.GetPlans;

namespace PropertyApi.Controllers;

/// <summary>
/// Public plan catalog — FRONTEND_BACKEND_CONTRACT.md §11.2. Marketing content
/// (/pricing), so anonymous like the rest of the public listing-browsing surface.
/// </summary>
[ApiController]
[Route("api/plans")]
public sealed class PlansController : ControllerBase
{
    private readonly ISender _sender;

    public PlansController(ISender sender)
        => _sender = sender;

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        var plans = await _sender.Send(new GetPlansQuery(), ct);
        return Ok(plans);
    }
}
