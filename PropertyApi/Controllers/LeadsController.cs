using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Marketing.Commands.SubmitLead;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Queries.GetLeads;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// Structured lead capture for marketing surfaces (today: the landing-page waitlist form)
/// — replaces the previous approach of posting this data to <c>POST /api/Contact</c> as a
/// free-text message, which left city/userType/offer attribution unqueryable.
/// </summary>
[ApiController]
[Route("api/leads")]
public sealed class LeadsController : ControllerBase
{
    private readonly ISender _sender;

    public LeadsController(ISender sender)
        => _sender = sender;

    [HttpPost]
    [AllowAnonymous] // Public waitlist/lead form; abuse is bounded by "leads-submit".
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("leads-submit")]
    public async Task<IActionResult> Submit([FromBody] LeadSubmitDto dto, CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await _sender.Send(
            new SubmitLeadCommand(
                dto.FullName,
                dto.Phone,
                dto.City,
                dto.UserType,
                Source: "landing-page",
                dto.Notes,
                dto.Campaign,
                dto.OfferId,
                HttpContext.Connection.RemoteIpAddress?.ToString()),
            ct);

        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? source = null,
        [FromQuery] string? userType = null,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(new GetLeadsQuery(page, pageSize, source, userType), ct);
        return Ok(result);
    }
}
