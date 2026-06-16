using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Bookings.Commands.CancelVisit;
using PropertyApi.Application.Bookings.Commands.ConfirmVisit;
using PropertyApi.Application.Bookings.Commands.DeclineVisit;
using PropertyApi.Application.Bookings.Commands.RequestVisit;
using PropertyApi.Application.Bookings.DTOs;
using PropertyApi.Application.Bookings.Queries.GetMyVisits;
using PropertyApi.Application.Bookings.Queries.GetPropertyVisits;
using PropertyApi.Domain.Bookings.Enums;

namespace PropertyApi.Controllers;

/// <summary>
/// Manages visit booking requests between visitors and property owners.
/// Rate limit "visits" applied to prevent booking spam.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class VisitsController : ControllerBase
{
    private readonly ISender _mediator;
    public VisitsController(ISender mediator) => _mediator = mediator;

    // ── GET /api/visits/mine ──────────────────────────────────────
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<VisitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyVisits(
        [FromQuery] VisitStatus? status,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _mediator.Send(
            new GetMyVisitsQuery(userId, status), ct);
        return Ok(result);
    }

    // ── GET /api/visits/property/{propertyId} ─────────────────────
    [HttpGet("property/{propertyId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<VisitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPropertyVisits(
        Guid propertyId, CancellationToken ct)
    {
        var ownerId = GetUserId();
        var result = await _mediator.Send(
            new GetPropertyVisitsQuery(propertyId, ownerId), ct);
        return Ok(result);
    }

    // ── POST /api/visits ──────────────────────────────────────────
    [HttpPost]
    [EnableRateLimiting("visits")]
    [ProducesResponseType(typeof(VisitDto), StatusCodes.Status201Created)]
    public new async Task<IActionResult> Request(
        [FromBody] RequestVisitRequest dto, CancellationToken ct)
    {
        var requesterId = GetUserId();
        var result = await _mediator.Send(
            new RequestVisitCommand(
                dto.PropertyId,
                requesterId,
                dto.ProposedAt,
                dto.VisitorName,
                dto.VisitorPhone,
                dto.VisitorNote), ct);
        return CreatedAtAction(nameof(GetMyVisits), result);
    }

    // ── PUT /api/visits/{id}/confirm ──────────────────────────────
    [HttpPut("{id:guid}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Confirm(
        Guid id, [FromBody] OwnerResponseRequest dto, CancellationToken ct)
    {
        await _mediator.Send(
            new ConfirmVisitCommand(id, GetUserId(), dto.Note), ct);
        return NoContent();
    }

    // ── PUT /api/visits/{id}/decline ──────────────────────────────
    [HttpPut("{id:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Decline(
        Guid id, [FromBody] OwnerResponseRequest dto, CancellationToken ct)
    {
        await _mediator.Send(
            new DeclineVisitCommand(id, GetUserId(), dto.Note), ct);
        return NoContent();
    }

    // ── DELETE /api/visits/{id} ───────────────────────────────────
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelVisitCommand(id, GetUserId()), ct);
        return NoContent();
    }

    // ── Private ───────────────────────────────────────────────────
    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(raw);
    }
}

// ── Request DTOs (simple, inline) ────────────────────────────────────────────
public sealed record RequestVisitRequest(
    Guid PropertyId,
    DateTime ProposedAt,
    string VisitorName,
    string VisitorPhone,
    string? VisitorNote
);

public sealed record OwnerResponseRequest(string? Note);
