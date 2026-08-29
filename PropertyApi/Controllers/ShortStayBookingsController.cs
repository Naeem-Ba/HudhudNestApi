using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.ShortStay.Commands.ApproveBooking;
using PropertyApi.Application.ShortStay.Commands.CancelBooking;
using PropertyApi.Application.ShortStay.Commands.CheckInBooking;
using PropertyApi.Application.ShortStay.Commands.CheckOutBooking;
using PropertyApi.Application.ShortStay.Commands.CompleteBooking;
using PropertyApi.Application.ShortStay.Commands.ConfirmBooking;
using PropertyApi.Application.ShortStay.Commands.CreateBooking;
using PropertyApi.Application.ShortStay.Commands.RecordBookingDepositPaid;
using PropertyApi.Application.ShortStay.Commands.RejectBooking;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Queries.GetHostBookingRequests;
using PropertyApi.Application.ShortStay.Queries.GetMyShortStayBookings;
using PropertyApi.Application.ShortStay.Queries.GetPricingPreview;
using PropertyApi.Application.ShortStay.Queries.GetUnitAvailability;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/short-stay/bookings")]
[Authorize]
public sealed class ShortStayBookingsController : ControllerBase
{
    private readonly ISender _mediator;
    public ShortStayBookingsController(ISender mediator) => _mediator = mediator;

    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<BookingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyShortStayBookingsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpGet("host-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<BookingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHostRequests(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetHostBookingRequestsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpGet("units/{unitId:guid}/availability")]
    [AllowAnonymous]
    [EnableRateLimiting("shortstay-search")]
    [ProducesResponseType(typeof(IReadOnlyList<UnitAvailabilityRangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability(
        Guid unitId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUnitAvailabilityQuery(unitId, from, to), ct);
        return Ok(result);
    }

    [HttpGet("pricing-preview")]
    [AllowAnonymous]
    [EnableRateLimiting("shortstay-search")]
    [ProducesResponseType(typeof(PricingBreakdownDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPricingPreview(
        [FromQuery] Guid unitId, [FromQuery] DateOnly checkIn, [FromQuery] DateOnly checkOut,
        [FromQuery] int adults, [FromQuery] int children, [FromQuery] int infants, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetPricingPreviewQuery(unitId, checkIn, checkOut, adults, children, infants), ct);
        return Ok(result);
    }

    [HttpPost]
    [EnableRateLimiting("shortstay-booking")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest dto, CancellationToken ct)
    {
        var command = new CreateBookingCommand(
            dto.UnitId, GetUserId(), dto.CheckIn, dto.CheckOut, dto.Adults, dto.Children, dto.Infants,
            dto.PaymentMethod, dto.HouseRulesAccepted);

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetMine), result);
    }

    [HttpPut("{id:guid}/approve")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] HostNoteRequest? dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveBookingCommand(id, GetUserId(), dto?.Note), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/reject")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] HostNoteRequest? dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectBookingCommand(id, GetUserId(), dto?.Note), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/record-deposit-paid")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordDepositPaid(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RecordBookingDepositPaidCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/confirm")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmBookingCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/check-in")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CheckInBookingCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/check-out")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckOut(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CheckOutBookingCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/complete")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteBookingCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] HostNoteRequest? dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelBookingCommand(id, GetUserId(), dto?.Note), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (Guid.TryParse(raw, out var userId))
            return userId;

        throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }
}

public sealed record CreateBookingRequest(
    Guid UnitId, DateOnly CheckIn, DateOnly CheckOut, int Adults, int Children, int Infants,
    string PaymentMethod, bool HouseRulesAccepted);

public sealed record HostNoteRequest(string? Note);
