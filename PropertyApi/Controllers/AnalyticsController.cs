using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Analytics.DTOs;
using PropertyApi.Application.Analytics.Queries.GetMarketInsights;
using PropertyApi.Application.Analytics.Queries.GetPropertyStats;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly ISender _mediator;
    public AnalyticsController(ISender mediator) => _mediator = mediator;

    // ── GET /api/analytics/property/{id} ─────────────────────────
    /// <summary>Statistics for a property — owner only.</summary>
    [HttpGet("property/{propertyId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(PropertyStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyStats(
        Guid propertyId, CancellationToken ct)
    {
        var ownerId = GetUserId();
        var result = await _mediator.Send(
            new GetPropertyStatsQuery(propertyId, ownerId), ct);
        return Ok(result);
    }

    // ── GET /api/analytics/market ─────────────────────────────────
    /// <summary>Public market insights — no auth required.</summary>
    [HttpGet("market")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]  // cache 5 min
    [ProducesResponseType(typeof(MarketInsightsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMarketInsights(
        [FromQuery] string? countryCode,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetMarketInsightsQuery(countryCode), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(raw);
    }
}