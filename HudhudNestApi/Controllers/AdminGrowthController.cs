using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Growth;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>Growth engine P10: admin growth overview (counts only). Metric definitions: docs/growth-engine/GROWTH-METRICS.md.</summary>
[ApiController]
[Route("api/admin/growth")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminGrowthController : ControllerBase
{
    private readonly ISender _sender;

    public AdminGrowthController(ISender sender) => _sender = sender;

    /// <summary>Totals, a daily series, the sign-up funnel, UTM campaigns and retention for [fromUtc, toUtc).</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(GrowthOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Overview([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
        => Ok(await _sender.Send(new GetGrowthOverviewQuery(fromUtc, toUtc), ct));
}
