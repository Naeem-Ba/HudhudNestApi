using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HudhudNestApi.Application.Valuation.Commands.SubmitOfficeResponse;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Application.Valuation.Queries.GetMyAgencyValuationInquiries;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Stage 6 — the office dashboard: an invited agency views and responds to the valuation
/// invitations sent to it.
///
/// Authorization note — same philosophy AgenciesController's own header comment states for
/// its invitation actions: there is no role that alone decides "does this invitation belong
/// to your office", so every action here is plain [Authorize] and the real check (the
/// caller's own UserAccount.AgencyId compared to the invitation's AgencyId) happens inside the
/// handler, never trusting an agency id the client could supply.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ValuationOfficeInvitationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ValuationOfficeInvitationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── GET /api/ValuationOfficeInvitations/mine ──────────────────
    // The caller's own agency's dashboard — resolved from the token, never from a
    // client-supplied agency id. Same "invitations/mine" shape AgenciesController uses for
    // its own invitation inbox.
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<ValuationOfficeInvitationDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var invitations = await _mediator.Send(new GetMyAgencyValuationInquiriesQuery(userId.Value), ct);

        return Ok(invitations);
    }

    // ── POST /api/ValuationOfficeInvitations/{invitationId}/respond ──
    // Any member of the invited agency may submit the estimate — the invitation targets the
    // office as an organization, not one specific person.
    [HttpPost("{invitationId:guid}/respond")]
    [ProducesResponseType(typeof(ValuationOfficeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Respond(
        Guid invitationId,
        [FromBody] SubmitOfficeResponseRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var response = await _mediator.Send(
            new SubmitOfficeResponseCommand(
                InvitationId: invitationId,
                ActorUserId: userId.Value,
                EstimatedPrice: request.EstimatedPrice,
                Notes: request.Notes),
            ct);

        return Ok(response);
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

/// <summary>Request body for POST .../respond — the estimate itself. InvitationId comes from
/// the route, ActorUserId from the token; neither can be supplied here.</summary>
public sealed record SubmitOfficeResponseRequest(decimal EstimatedPrice, string? Notes);
