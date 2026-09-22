using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Bookings.Commands.AcceptRescheduledVisit;
using HudhudNestApi.Application.Bookings.Commands.CancelVisit;
using HudhudNestApi.Application.Bookings.Commands.CompleteVisit;
using HudhudNestApi.Application.Bookings.Commands.ConfirmVisit;
using HudhudNestApi.Application.Bookings.Commands.DeclineRescheduledVisit;
using HudhudNestApi.Application.Bookings.Commands.DeclineVisit;
using HudhudNestApi.Application.Bookings.Commands.ProposeAlternateVisit;
using HudhudNestApi.Application.Bookings.Commands.RequestVisit;
using HudhudNestApi.Application.Bookings.DTOs;
using HudhudNestApi.Application.Bookings.Queries.GetMyVisits;
using HudhudNestApi.Application.Bookings.Queries.GetPropertyVisits;
using HudhudNestApi.Domain.Bookings.Enums;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class VisitsController : ControllerBase
{
    private readonly ISender _mediator;

    public VisitsController(ISender mediator) => _mediator = mediator;

    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<VisitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyVisits(
        [FromQuery] VisitStatus? status,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetMyVisitsQuery(GetUserId(), status), ct);

        return Ok(result);
    }

    [HttpGet("property/{propertyId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<VisitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyVisits(
        Guid propertyId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetPropertyVisitsQuery(propertyId, GetUserId()), ct);

        return Ok(result);
    }

    [HttpPost]
    [EnableRateLimiting("visits")]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> RequestVisit(
        [FromBody] RequestVisitRequest dto,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RequestVisitCommand(
                dto.PropertyId,
                GetUserId(),
                dto.ProposedAt,
                dto.VisitorName,
                dto.VisitorPhone,
                dto.VisitorNote), ct);

        return CreatedAtAction(nameof(GetMyVisits), result);
    }

    [HttpPut("{id:guid}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Confirm(
        Guid id,
        [FromBody] OwnerResponseRequest dto,
        CancellationToken ct)
    {
        await _mediator.Send(new ConfirmVisitCommand(id, GetUserId(), dto.Note), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Decline(
        Guid id,
        [FromBody] OwnerResponseRequest dto,
        CancellationToken ct)
    {
        await _mediator.Send(new DeclineVisitCommand(id, GetUserId(), dto.Note), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/propose-alternate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProposeAlternate(
        Guid id,
        [FromBody] ProposeAlternateRequest dto,
        CancellationToken ct)
    {
        await _mediator.Send(
            new ProposeAlternateVisitCommand(id, GetUserId(), dto.ProposedAt, dto.Note), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/accept-reschedule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptReschedule(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new AcceptRescheduledVisitCommand(id, GetUserId()), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/decline-reschedule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineReschedule(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeclineRescheduledVisitCommand(id, GetUserId()), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CompleteVisitCommand(id, GetUserId()), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelVisitCommand(id, GetUserId()), ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (Guid.TryParse(raw, out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }
}

public sealed record RequestVisitRequest(
    Guid PropertyId,
    DateTime ProposedAt,
    string VisitorName,
    string VisitorPhone,
    string? VisitorNote);

public sealed record OwnerResponseRequest(string? Note);

public sealed record ProposeAlternateRequest(DateTime ProposedAt, string? Note);
