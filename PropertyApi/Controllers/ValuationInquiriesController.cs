using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Valuation.Commands.CreateValuationInquiry;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Queries.GetValuationInquiryStatus;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Controllers;

/// <summary>
/// Stage 7 — the customer-facing entry point into the Valuation module: submit a request
/// (Fast Path +, if needed, office matching run synchronously inside the same call) and
/// check on it later. Both actions are anonymous by design — ValuationInquiry.RequesterId is
/// nullable specifically for a guest visitor (see its own doc comment) — but still resolve
/// and record the caller's id when they happen to be authenticated.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ValuationInquiriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ValuationInquiriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ── POST /api/ValuationInquiries ──────────────────────────────
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("valuation-inquiries")]
    [ProducesResponseType(typeof(CreateValuationInquiryResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateValuationInquiryRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateValuationInquiryCommand(
                RequesterId: GetCurrentUserId(),
                GovernorateId: request.GovernorateId,
                DistrictId: request.DistrictId,
                NeighborhoodId: request.NeighborhoodId,
                PropertyTypeId: request.PropertyTypeId,
                Area: request.Area,
                Rooms: request.Rooms,
                RequestType: request.RequestType),
            ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    // ── GET /api/ValuationInquiries/{inquiryId} ───────────────────
    // Anonymous by design (a guest who just submitted one still needs to check it), but the
    // handler still enforces ownership whenever the inquiry actually belongs to an account —
    // see GetValuationInquiryStatusQueryHandler's own authorization comment.
    [HttpGet("{inquiryId:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(typeof(ValuationInquiryStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid inquiryId, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetValuationInquiryStatusQuery(inquiryId, GetCurrentUserId()), ct);

        return Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

/// <summary>Request body for POST /api/ValuationInquiries. RequesterId is deliberately absent
/// here — it is resolved from the auth token, never supplied by the client.</summary>
public sealed record CreateValuationInquiryRequest(
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? Area,
    int? Rooms,
    ListingType RequestType);
