using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using HudhudNestApi.Configuration;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Commands.ConfirmFeaturedListingPayment;
using HudhudNestApi.Application.Listings.Commands.ConfirmListingExtensionPayment;
using HudhudNestApi.Application.Listings.Commands.ConfirmPropertyAvailability;
using HudhudNestApi.Application.Listings.Commands.CreateProperty;
using HudhudNestApi.Application.Listings.Commands.RequestFeaturedListing;
using HudhudNestApi.Application.Listings.Commands.RequestListingExtension;
using HudhudNestApi.Application.Listings.Commands.TrackPropertyShareEvent;
using HudhudNestApi.Application.Listings.Commands.TrackPropertyAttributionEvent;
using HudhudNestApi.Application.Listings.Queries.CheckPotentialDuplicateProperty;
using HudhudNestApi.Application.Listings.Queries.GetSocialPerformance;
using HudhudNestApi.Application.Listings.Commands.DeleteProperty;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Commands.PublishProperty;
using HudhudNestApi.Application.Listings.Queries.GetPropertiesList;
using HudhudNestApi.Application.Listings.Queries.GetPropertyById;
using HudhudNestApi.Application.Listings.Queries.GetPropertyForManagement;
using HudhudNestApi.Application.Listings.Queries.GetMyProperties;
using HudhudNestApi.Application.Listings.Queries.SearchPropertiesNearby;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Controllers;

/// <summary>
/// Property listing endpoints (replaced the legacy listings controller).
///
/// CHANGES from old controller:
/// 1. Uses MediatR instead of calling a service directly
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
    [OutputCache(PolicyName = OutputCacheRegistration.PublicPropertyListPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] PropertyFilterDto filter,
        CancellationToken ct)
    {
        // [OutputCache] caches server-side (Redis) but never sends Cache-Control to the
        // browser/CDN (see AnalyticsController's doc comment) -- set explicitly here, same
        // duration as the policy's own Expire() (see OutputCacheRegistration.PublicPropertyListMaxAge
        // for why reusing that exact duration doesn't add staleness exposure).
        SetPublicCacheControl(OutputCacheRegistration.PublicPropertyListMaxAge);

        var result = await _mediator.Send(new GetPropertiesListQuery(filter), ct);
        return Ok(result);
    }

    // ── GET /api/properties/geo-search?latitude=51.45&longitude=7.01&radiusKm=5 ──
    [HttpGet("geo-search")]
    [AllowAnonymous]
    [EnableRateLimiting("geo-search")]
    [OutputCache(PolicyName = OutputCacheRegistration.PublicPropertyGeoSearchPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchNearby(
    [FromQuery] GeoPropertySearchRequestDto filter,
    CancellationToken ct)
    {
        var result = await _mediator.Send(new SearchPropertiesNearbyQuery(filter), ct);

        // Same browser/CDN window as the list: the server-side TTL already defines the staleness
        // every visitor accepts (see OutputCacheRegistration.PublicPropertyListMaxAge). Set only
        // after the query succeeded so a rejected request (400) is never marked cacheable.
        SetPublicCacheControl(OutputCacheRegistration.PublicPropertyListMaxAge);
        return Ok(result);
    }

    // ── GET /api/properties/{id} ─────────────────────────────────
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [OutputCache(PolicyName = OutputCacheRegistration.PublicPropertyDetailsPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        SetPublicCacheControl(OutputCacheRegistration.PublicPropertyDetailsMaxAge);

        var result = await _mediator.Send(new GetPropertyByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    private void SetPublicCacheControl(TimeSpan maxAge)
    {
        Response.GetTypedHeaders().CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
        {
            Public = true,
            MaxAge = maxAge
        };
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
    [EnableRateLimiting("user-write")]
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
    [EnableRateLimiting("user-write")]
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
    [EnableRateLimiting("user-write")]
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

    // ── POST /api/properties/{id}/share-events ───────────────────
    // Social Sharing & Distribution — records that a visitor successfully shared (or copied
    // the link to) this listing. Public: most sharers are never logged in, so this cannot be
    // [Authorize]d — UserId is attached only when a valid access token happens to be present.
    // {id} always comes from the route (never the body), and the handler independently
    // re-checks the listing is still published/unexpired before writing anything — the fact
    // that a share button was rendered client-side five minutes ago proves nothing by itself.
    [HttpPost("{id:guid}/share-events")]
    [AllowAnonymous]
    [EnableRateLimiting("property-share-events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TrackShareEvent(
        Guid id,
        [FromBody] PropertyShareEventSubmitDto dto,
        CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        // [Required] never fires for a non-nullable enum — see the identical guard in
        // MarketingEventsController.Track for why this explicit check is still needed.
        if (!Enum.IsDefined(dto.Platform))
            return BadRequest(new { message = "Platform is required and must be a known share platform." });

        var shareEventId = await _mediator.Send(
            new TrackPropertyShareEventCommand(
                id,
                dto.Platform,
                GetCurrentUserId(),
                dto.UtmSource,
                dto.UtmMedium,
                dto.UtmCampaign,
                dto.UtmContent),
            ct);

        return Ok(new { success = true, id = shareEventId });
    }

    // UTM / Attribution (Phase 2) — records what happened *after* a visitor opened a
    // (possibly attributed) property link: a page view, or a contact/lead action. Same public/
    // rate-limited/re-validated shape as TrackShareEvent above; kept as its own endpoint/table
    // rather than widening share-events, since a view or a contact click is not a share.
    [HttpPost("{id:guid}/attribution-events")]
    [AllowAnonymous]
    [EnableRateLimiting("property-attribution-events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TrackAttributionEvent(
        Guid id,
        [FromBody] PropertyAttributionEventSubmitDto dto,
        CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        if (!Enum.IsDefined(dto.EventType))
            return BadRequest(new { message = "EventType is required and must be a known attribution event type." });

        var attributionEventId = await _mediator.Send(
            new TrackPropertyAttributionEventCommand(
                id,
                dto.EventType,
                GetCurrentUserId(),
                dto.UtmSource,
                dto.UtmMedium,
                dto.UtmCampaign,
                dto.UtmContent),
            ct);

        return Ok(new { success = true, id = attributionEventId });
    }

    // ── GET /api/properties/social-performance ───────────────────
    // Phase 14 spec §10 "Social Performance" dashboard — real Share/Attribution event
    // aggregates, admin-only (never exposed anonymously: even aggregate counts are reporting
    // data, not public listing content).
    [HttpGet("social-performance")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(SocialPerformanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSocialPerformance(
        [FromQuery] Guid? propertyId, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSocialPerformanceQuery(propertyId, fromUtc, toUtc), ct);
        return Ok(result);
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
