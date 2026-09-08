using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Marketing.Commands.CreateOffer;
using PropertyApi.Application.Marketing.Commands.SetOfferStatus;
using PropertyApi.Application.Marketing.Commands.UpdateOffer;
using PropertyApi.Application.Marketing.DTOs;
using PropertyApi.Application.Marketing.Queries.GetActiveOffer;
using PropertyApi.Application.Marketing.Queries.GetOffers;
using PropertyApi.Domain.Marketing.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

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
                dto.Terms),
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
                dto.Terms),
            ct);

        return updated ? NoContent() : NotFound();
    }

    /// <summary>Body is the bare enum name: "Active" | "Paused" | "Ended".</summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStatus(
        Guid id,
        [FromBody] OfferStatus status,
        CancellationToken ct)
    {
        try
        {
            var updated = await _sender.Send(new SetOfferStatusCommand(id, status), ct);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
