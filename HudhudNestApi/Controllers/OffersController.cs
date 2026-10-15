using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Marketing.Commands.CreateOffer;
using HudhudNestApi.Application.Marketing.Commands.SetOfferStatus;
using HudhudNestApi.Application.Marketing.Commands.UpdateOffer;
using HudhudNestApi.Application.Marketing.DTOs;
using HudhudNestApi.Application.Marketing.Queries.GetActiveOffer;
using HudhudNestApi.Application.Marketing.Queries.GetOffers;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Marketing offers (e.g. "first 100 agencies"). No billing/payment gateway is involved —
/// FRONTEND_BACKEND_CONTRACT.md §11.5 — this only tracks the commitment and a real,
/// server-enforced redemption cap; honoring it happens manually once a Lead converts.
/// </summary>
[ApiController]
[Route("api/offers")]
public sealed class OffersController : ControllerBase
{
    private readonly ISender _sender;

    public OffersController(ISender sender)
        => _sender = sender;

    /// <summary>Public: the one currently redeemable offer, or 204 if none. The landing
    /// page must hide its offer section entirely on 204 rather than show anything stale.</summary>
    [HttpGet("active")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var offer = await _sender.Send(new GetActiveOfferQuery(), ct);
        return offer is null ? NoContent() : Ok(offer);
    }

    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var offers = await _sender.Send(new GetOffersQuery(), ct);
        return Ok(offers);
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] OfferSubmitDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var id = await _sender.Send(
            new CreateOfferCommand(
                dto.Name,
                dto.DiscountType,
                dto.DiscountValue,
                dto.StartsAtUtc,
                dto.Description,
                dto.TargetPlanTier,
                dto.EndsAtUtc,
                dto.MaxRedemptions,
                dto.Terms,
                GetUserId(),
                GetIpAddress()),
            ct);

        return CreatedAtAction(nameof(GetAll), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] OfferSubmitDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var updated = await _sender.Send(
            new UpdateOfferCommand(
                id,
                dto.Name,
                dto.DiscountType,
                dto.DiscountValue,
                dto.StartsAtUtc,
                dto.Description,
                dto.TargetPlanTier,
                dto.EndsAtUtc,
                dto.MaxRedemptions,
                dto.Terms,
                GetUserId(),
                GetIpAddress()),
            ct);

        return updated ? NoContent() : NotFound();
    }

    /// <summary>
    /// Body: <c>{ "status": "Active" | "Paused" | "Ended" }</c> as application/json. Returns the
    /// offer as it is now (including <c>isPubliclyVisible</c>). A transition the lifecycle does
    /// not allow (re-activating an ended offer, activating an expired one) answers 400 with an
    /// Arabic <c>message</c>; repeating the current status is a harmless 200.
    /// </summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = RoleNames.Admin)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(OfferAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> SetStatus(
        Guid id,
        [FromBody] SetOfferStatusRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (request.Status is not { } status || !Enum.IsDefined(status))
            return BadRequest(new { message = "حالة العرض غير معروفة. القيم المسموحة: Active أو Paused أو Ended." });

        try
        {
            var offer = await _sender.Send(
                new SetOfferStatusCommand(id, status, GetUserId(), GetIpAddress()), ct);
            return offer is null ? NotFound() : Ok(offer);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (Guid.TryParse(raw, out var id))
            return id;

        throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
