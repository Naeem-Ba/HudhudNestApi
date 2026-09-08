using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Marketing.Commands.TrackMarketingEvent;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Queries.GetMarketingEventsSummary;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// Conversion-funnel tracking for marketing surfaces. Deliberately named "marketing-events"
/// rather than "analytics" — <c>AnalyticsController</c>/<c>api/analytics</c> already exists
/// in this codebase for an unrelated concern (property/market statistics); reusing that name
/// here would collide in meaning, not just in route.
/// </summary>
[ApiController]
[Route("api/marketing-events")]
public sealed class MarketingEventsController : ControllerBase
{
    private readonly ISender _sender;

    public MarketingEventsController(ISender sender)
        => _sender = sender;

    [HttpPost]
    [AllowAnonymous] // Public funnel tracking; abuse is bounded by "marketing-events".
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("marketing-events")]
    public async Task<IActionResult> Track([FromBody] MarketingEventSubmitDto dto, CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // [Required] never fires for a non-nullable enum (it cannot be null, so the
        // attribute is always "satisfied") — an omitted eventType silently becomes the
        // undefined default(MarketingEventType) = 0 instead of failing validation. Guard
        // explicitly against that, and against any other out-of-range integer a caller
        // might send.
        if (!Enum.IsDefined(typeof(Domain.Marketing.Enums.MarketingEventType), dto.EventType))
            return BadRequest(new { message = "EventType is required and must be a known event type." });

        var id = await _sender.Send(
            new TrackMarketingEventCommand(
                dto.EventType,
                Source: "landing-page",
                dto.Campaign,
                dto.SessionId,
                dto.Path,
                dto.LeadId,
                dto.OfferId),
            ct);

        return Ok(new { success = true, id });
    }

    [HttpGet("summary")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetMarketingEventsSummaryQuery(fromUtc, toUtc), ct);
        return Ok(result);
    }
}
