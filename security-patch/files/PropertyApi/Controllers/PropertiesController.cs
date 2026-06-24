using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Application.Listings.Commands.DeleteProperty;
using PropertyApi.Application.Listings.Commands.UpdateProperty;
using PropertyApi.Application.Listings.Queries.GetPropertiesList;
using PropertyApi.Application.Listings.Queries.GetPropertyById;
using PropertyApi.Application.Listings.Queries.SearchPropertiesNearby;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;

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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPropertyByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
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
            new { id = propertyId });
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
            PropertyId       = id,
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
    [HttpPatch("{id:guid}/publish")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var success = await _mediator.Send(new UpdatePropertyCommand(
            PropertyId       : id,
            RequestingUserId : userId.Value,
            Title            : null, Description: null,
            Street: null, City: null, Region: null,
            CountryCode: null, PostalCode: null,
            Latitude: null, Longitude: null,
            ColdRent: null, WarmRent: null, PurchasePrice: null,
            Deposit: null, AdditionalCosts: null, CurrencyCode: null,
            Rooms: null, Area: null, Floor: null, TotalFloors: null,
            HasBalcony: null, HasElevator: null, HasParkingSpace: null,
            HeatingType: null, Status: null, Condition: null,
            EnergyEfficiency: null, AvailableFrom: null, ExpiresAt: null,
            IsPublished      : true
        ), ct);

        return success ? NoContent() : NotFound();
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
