using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Commands.ConfirmFeaturedListingPayment;
using PropertyApi.Application.Listings.Commands.ConfirmListingExtensionPayment;
using PropertyApi.Application.Listings.Commands.ConfirmPropertyAvailability;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Application.Listings.Commands.RequestFeaturedListing;
using PropertyApi.Application.Listings.Commands.RequestListingExtension;
using PropertyApi.Application.Listings.Queries.CheckPotentialDuplicateProperty;
using PropertyApi.Application.Listings.Commands.DeleteProperty;
using PropertyApi.Application.Listings.Commands.UpdateProperty;
using PropertyApi.Application.Listings.Commands.PublishProperty;
using PropertyApi.Application.Listings.Queries.GetPropertiesList;
using PropertyApi.Application.Listings.Queries.GetPropertyById;
using PropertyApi.Application.Listings.Queries.GetPropertyForManagement;
using PropertyApi.Application.Listings.Queries.GetMyProperties;
using PropertyApi.Application.Listings.Queries.SearchPropertiesNearby;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Controllers;

/// <summary>
/// Replaces the old WohnungenController.
///
/// CHANGES from old controller:
/// 1. Uses MediatR instead of IWohnungService directly
/// 2. Uses Guid IDs everywhere (old used int IDs)
/// 3. Structured query parameters via [FromQuery] PropertyFilterDto
/// 4. Proper 404 / 403 / 200 response handling
/// 5. OwnerId read from JWT claim (Guid), not int.TryParse
/// 6. POST returns 201 Created with Location header (REST standard)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PropertiesController : ControllerBase
{
    private readonly ISender _mediator;

    public PropertiesController(ISender mediator)
        => _mediator = mediator;

    // ── GET /api/properties?city=Berlin&minRooms=2&page=1 ───────
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("public-search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] PropertyFilterDto filter,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPropertiesListQuery(filter), ct);
        return Ok(result);
    }

    // ── GET /api/properties/geo-search?latitude=51.45&longitude=7.01&radiusKm=5 ──
    [HttpGet("geo-search")]
    [AllowAnonymous]
    [EnableRateLimiting("geo-search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchNearby(
    [FromQuery] GeoPropertySearchRequestDto filter,
    CancellationToken ct)
    {
        var result = await _mediator.Send(new SearchPropertiesNearbyQuery(filter), ct);
        return Ok(result);
    }

    // ── GET /api/properties/{id} ─────────────────────────────────
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPropertyByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _mediator.Send(
            new GetMyPropertiesQuery(userId.Value),
            ct);

        return Ok(result);
    }

    [HttpGet("{id:guid}/manage")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForManagement(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _mediator.Send(
            new GetPropertyForManagementQuery(
                id,
                userId.Value,
                User.IsInRole(RoleNames.Admin)),
            ct);

        return Ok(result);
    }

    // ── GET /api/properties/check-duplicate ──────────────────────
    // Advisory only — never blocks. Called by the frontend before final submit
    // (property-form) to surface a dismissible "this might already exist" prompt.
    // See CheckPotentialDuplicatePropertyQuery for why this is a Query, not a
    // blocking CreatePropertyCommandValidator rule.
    [HttpGet("check-duplicate")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckDuplicate(
        [FromQuery] int? neighborhoodId,
        [FromQuery] decimal? area,
        [FromQuery] decimal? price,
        [FromQuery] ListingType listingType,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _mediator.Send(
            new CheckPotentialDuplicatePropertyQuery(
                userId.Value,
                neighborhoodId,
                area,
                price,
                listingType),
            ct);

        return Ok(result);
    }

    // ── POST /api/properties ─────────────────────────────────────
    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePropertyCommand command,
        CancellationToken ct)
    {
        // Override OwnerId with the authenticated user's ID
        // (never trust client-supplied OwnerId — security risk)
        var ownerId = GetCurrentUserId();
        if (ownerId is null) return Unauthorized();

        var cmd = command with { OwnerId = ownerId.Value };
        var propertyId = await _mediator.Send(cmd, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = propertyId },
            new
            {
                id = propertyId,
                isPublished = true,
                status = "Published",
                nextAction = "UploadImages"
            });
    }

    // ── PUT /api/properties/{id} ─────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePropertyCommand command,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var cmd = command with
        {
            PropertyId = id,
            RequestingUserId = userId.Value
        };

        var success = await _mediator.Send(cmd, ct);
        return success ? NoContent() : NotFound();
    }

    // ── DELETE /api/properties/{id} ──────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var success = await _mediator.Send(
            new DeletePropertyCommand(id, userId.Value, GetClientIp()), ct);

        return success ? NoContent() : NotFound();
    }

    // ── PATCH /api/properties/{id}/publish ───────────────────────
    [HttpPost("{id:guid}/publish")]
    [HttpPatch("{id:guid}/publish")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await _mediator.Send(
            new PublishPropertyCommand(
                id,
                userId.Value,
                User.IsInRole(RoleNames.Admin)),
            ct);

        return NoContent();
    }

    // ── PATCH /api/properties/{id}/confirm-availability ─────────
    // Phase-0, Task 2 — single-tap "still available?" confirmation.
    [HttpPatch("{id:guid}/confirm-availability")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmAvailability(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await _mediator.Send(
            new ConfirmPropertyAvailabilityCommand(
                id,
                userId.Value,
                User.IsInRole(RoleNames.Admin)),
            ct);

        return NoContent();
    }

    // ── POST /api/properties/{id}/extension ──────────────────────
    // Owner asks to extend one expired (or nearly expired) listing for a fee.
    //
    // 202 Accepted, not 200 OK, and deliberately so: this records what the owner owes and
    // nothing more. The listing is NOT extended by this call. No payment gateway exists in
    // this system, so the fee stays Pending until something settles it — today an
    // administrator via the endpoint below, later a gateway callback.
    [HttpPost("{id:guid}/extension")]
    [Authorize]
    [ProducesResponseType(typeof(ListingExtensionQuoteDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestExtension(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var quote = await _mediator.Send(
            new RequestListingExtensionCommand(id, userId.Value),
            ct);

        return Accepted(quote);
    }

    // ── POST /api/properties/extension/{transactionId}/confirm ───
    // Settles a pending extension fee and grants the listing another publication period.
    //
    // Admin-only because it is the only way to turn an unpaid fee into a paid one while
    // there is no gateway. Exposing it to the owner would make the fee optional.
    [HttpPost("extension/{transactionId:guid}/confirm")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmExtensionPayment(Guid transactionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var newExpiresAt = await _mediator.Send(
            new ConfirmListingExtensionPaymentCommand(transactionId, userId.Value),
            ct);

        return Ok(new { expiresAt = newExpiresAt });
    }

    // ── POST /api/properties/{id}/featured ───────────────────────
    // Owner asks to promote one listing to featured placement for a fee.
    //
    // 202 Accepted for the same reason as the extension endpoint above: this records what
    // the owner owes and nothing more. The listing is NOT promoted by this call.
    [HttpPost("{id:guid}/featured")]
    [Authorize]
    [ProducesResponseType(typeof(FeaturedListingQuoteDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestFeatured(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var quote = await _mediator.Send(
            new RequestFeaturedListingCommand(id, userId.Value),
            ct);

        return Accepted(quote);
    }

    // ── POST /api/properties/featured/{transactionId}/confirm ────
    // Settles a pending featured fee and promotes the listing.
    //
    // Admin-only, for the same reason the extension confirmation is: with no gateway, this
    // is the only thing that can turn an unpaid fee into a paid one. Exposed to the owner it
    // would make featured placement free.
    [HttpPost("featured/{transactionId:guid}/confirm")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmFeaturedPayment(Guid transactionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var featuredUntil = await _mediator.Send(
            new ConfirmFeaturedListingPaymentCommand(transactionId, userId.Value),
            ct);

        return Ok(new { featuredUntil });
    }

    // ── Helper ───────────────────────────────────────────────────
    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}
