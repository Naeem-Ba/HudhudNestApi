using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.ShortStay.Commands.AddAccommodationUnit;
using HudhudNestApi.Application.ShortStay.Commands.AddRoomType;
using HudhudNestApi.Application.ShortStay.Commands.AddShortStayListingPhotos;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.ShortStay.Commands.CreateShortStayListing;
using HudhudNestApi.Application.ShortStay.Commands.DeleteShortStayListing;
using HudhudNestApi.Application.ShortStay.Commands.ManageUnitAvailability;
using HudhudNestApi.Application.ShortStay.Commands.PublishShortStayListing;
using HudhudNestApi.Application.ShortStay.Commands.SetMinimumStayRules;
using HudhudNestApi.Application.ShortStay.Commands.SetPricingRules;
using HudhudNestApi.Application.ShortStay.Commands.SetShortStayListingAmenities;
using HudhudNestApi.Application.ShortStay.Commands.UpdateShortStayListing;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Queries.GetActiveAccommodationTypes;
using HudhudNestApi.Application.ShortStay.Queries.GetMyShortStayListings;
using HudhudNestApi.Application.ShortStay.Queries.GetShortStayListingById;
using HudhudNestApi.Application.ShortStay.Queries.GetShortStayListingReviews;
using HudhudNestApi.Application.ShortStay.Queries.SearchShortStayListings;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/short-stay/listings")]
public sealed class ShortStayListingsController : ControllerBase
{
    private readonly ISender _mediator;
    public ShortStayListingsController(ISender mediator) => _mediator = mediator;

    [HttpGet("accommodation-types")]
    [AllowAnonymous]
    [EnableRateLimiting("shortstay-search")]
    [ProducesResponseType(typeof(IReadOnlyList<AccommodationTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccommodationTypes(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetActiveAccommodationTypesQuery(), ct);
        return Ok(result);
    }

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("shortstay-search")]
    [ProducesResponseType(typeof(Application.Properties.DTOs.PagedResult<ShortStayListingSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? city,
        [FromQuery] int? accommodationTypeId,
        [FromQuery] DateOnly? checkIn,
        [FromQuery] DateOnly? checkOut,
        [FromQuery] int? guests,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = new ShortStayListingSearchFilter
        {
            City = city,
            AccommodationTypeId = accommodationTypeId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Guests = guests,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _mediator.Send(new SearchShortStayListingsQuery(filter), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting("shortstay-search")]
    [ProducesResponseType(typeof(ShortStayListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var userId = TryGetUserId();
        var result = await _mediator.Send(new GetShortStayListingByIdQuery(id, userId), ct);
        return Ok(result);
    }

    /// <summary>The reviews of a published listing with their average (public).</summary>
    [HttpGet("{id:guid}/reviews")]
    [AllowAnonymous]
    [EnableRateLimiting("shortstay-search")]
    [ProducesResponseType(typeof(ShortStayReviewSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviews(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetShortStayListingReviewsQuery(id, page, pageSize), ct));

    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<ShortStayListingSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyShortStayListingsQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ShortStayListingDto), StatusCodes.Status201Created)]
    [EnableRateLimiting("user-write")]
    public async Task<IActionResult> Create([FromBody] CreateShortStayListingRequest dto, CancellationToken ct)
    {
        var command = new CreateShortStayListingCommand(
            GetUserId(), dto.AccommodationTypeId, dto.Title, dto.Description, dto.Capacity, dto.Bedrooms,
            dto.Bathrooms, dto.CheckInTime, dto.CheckOutTime, dto.Latitude, dto.Longitude,
            dto.DefaultBasePricePerNight, dto.PropertyId, dto.CurrencyCode, dto.GovernorateId, dto.DistrictId,
            dto.NeighborhoodId, dto.City, dto.LocationVisibility);

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ShortStayListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EnableRateLimiting("user-write")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShortStayListingRequest dto, CancellationToken ct)
    {
        var command = new UpdateShortStayListingCommand(
            id, GetUserId(), dto.Title, dto.Description, dto.Capacity, dto.Bedrooms, dto.Bathrooms,
            dto.CheckInTime, dto.CheckOutTime, dto.SelfCheckInEnabled, dto.InstantBookingEnabled,
            dto.RequestBookingEnabled, dto.Latitude, dto.Longitude, dto.GovernorateId, dto.DistrictId,
            dto.NeighborhoodId, dto.City, dto.LocationVisibility, dto.PoolType, dto.PoolLocation,
            dto.PoolIsSeasonal, dto.PoolIsHeated, dto.CleaningFee, dto.ExtraGuestFee, dto.ExtraBedFee,
            dto.AllowsSmoking, dto.AllowsParties, dto.AllowsPets, dto.QuietHoursStart, dto.QuietHoursEnd,
            dto.CustomRulesText, dto.CancellationFreeCancellationDays, dto.CancellationDepositRefundable,
            dto.CancellationCustomTermsText, dto.DepositPercentage);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    // 409: the listing still has bookings in progress (pending .. checked out).
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await _mediator.Send(
            new DeleteShortStayListingCommand(id, GetUserId(), GetClientIp()), ct);

        return success ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/publish")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new PublishShortStayListingCommand(id, GetUserId()), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/unpublish")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new UnpublishShortStayListingCommand(id, GetUserId()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/photos")]
    [Authorize]
    [RequestSizeLimit(20_000_000)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EnableRateLimiting("user-write")]
    public async Task<IActionResult> UploadPhotos(Guid id, [FromForm] IFormFileCollection files, CancellationToken ct)
    {
        var uploadFiles = (files ?? new FormFileCollection())
            .Select(file => new UploadPropertyImageFileDto(
                file.OpenReadStream(), file.FileName, file.ContentType, file.Length))
            .ToList();

        var result = await _mediator.Send(new AddShortStayListingPhotosCommand(id, GetUserId(), uploadFiles), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/amenities")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetAmenities(Guid id, [FromBody] IReadOnlyList<Guid> amenityIds, CancellationToken ct)
    {
        await _mediator.Send(new SetShortStayListingAmenitiesCommand(id, GetUserId(), amenityIds), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/room-types")]
    [Authorize]
    [ProducesResponseType(typeof(RoomTypeDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddRoomType(Guid id, [FromBody] AddRoomTypeRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AddRoomTypeCommand(id, GetUserId(), dto.Name, dto.BasePricePerNight, dto.Capacity), ct);
        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    [HttpPost("room-types/{roomTypeId:guid}/units")]
    [Authorize]
    [ProducesResponseType(typeof(AccommodationUnitDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddUnit(Guid roomTypeId, [FromBody] AddAccommodationUnitRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddAccommodationUnitCommand(roomTypeId, GetUserId(), dto.Label), ct);
        return CreatedAtAction(nameof(GetById), new { id = roomTypeId }, result);
    }

    // ── Availability the host controls (owner decision 2026-10-18) ─────────

    /// <summary>The host's manual blocks of a unit (periods marked as booked or unavailable), from today on.</summary>
    [HttpGet("units/{unitId:guid}/blocks")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<UnitBlockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnitBlocks(Guid unitId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetUnitBlocksQuery(unitId, GetUserId()), ct));

    /// <summary>Marks [from, to) as not available. To is exclusive (the morning the unit is free again). 409 over a booking or another block.</summary>
    [HttpPost("units/{unitId:guid}/blocks")]
    [Authorize]
    [ProducesResponseType(typeof(UnitBlockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddUnitBlock(Guid unitId, [FromBody] AddUnitBlockRequest dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddUnitBlockCommand(unitId, GetUserId(), dto.From, dto.To), ct);
        return CreatedAtAction(nameof(GetUnitBlocks), new { unitId }, result);
    }

    [HttpDelete("units/{unitId:guid}/blocks/{blockId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveUnitBlock(Guid unitId, Guid blockId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveUnitBlockCommand(unitId, blockId, GetUserId()), ct);
        return NoContent();
    }

    /// <summary>"Not available for now": switches a unit off or on. Bookings it already has are untouched.</summary>
    [HttpPut("units/{unitId:guid}/availability")]
    [Authorize]
    [ProducesResponseType(typeof(AccommodationUnitDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetUnitAvailability(Guid unitId, [FromBody] SetUnitAvailabilityRequest dto, CancellationToken ct)
        => Ok(await _mediator.Send(new SetUnitActiveCommand(unitId, GetUserId(), dto.IsActive), ct));

    [HttpPut("room-types/{roomTypeId:guid}/pricing-rules")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPricingRules(
        Guid roomTypeId, [FromBody] IReadOnlyList<PricingRuleInput> rules, CancellationToken ct)
    {
        await _mediator.Send(new SetPricingRulesCommand(roomTypeId, GetUserId(), rules), ct);
        return NoContent();
    }

    [HttpPut("room-types/{roomTypeId:guid}/minimum-stay-rules")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetMinimumStayRules(
        Guid roomTypeId, [FromBody] IReadOnlyList<MinimumStayRuleInput> rules, CancellationToken ct)
    {
        await _mediator.Send(new SetMinimumStayRulesCommand(roomTypeId, GetUserId(), rules), ct);
        return NoContent();
    }

    private Guid? TryGetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private Guid GetUserId() => TryGetUserId()
        ?? throw new UnauthorizedAccessException("Missing or invalid authenticated user id claim.");

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record CreateShortStayListingRequest(
    int AccommodationTypeId, string Title, string Description, int Capacity, int Bedrooms, int Bathrooms,
    TimeOnly CheckInTime, TimeOnly CheckOutTime, decimal? Latitude, decimal? Longitude,
    decimal DefaultBasePricePerNight, Guid? PropertyId, string? CurrencyCode = null,
    int? GovernorateId = null, int? DistrictId = null, int? NeighborhoodId = null, string? City = null,
    string? LocationVisibility = null);

public sealed record UpdateShortStayListingRequest(
    string Title, string Description, int Capacity, int Bedrooms, int Bathrooms, TimeOnly CheckInTime,
    TimeOnly CheckOutTime, bool SelfCheckInEnabled, bool InstantBookingEnabled, bool RequestBookingEnabled,
    decimal? Latitude, decimal? Longitude, int? GovernorateId, int? DistrictId, int? NeighborhoodId,
    string? City, string LocationVisibility, string? PoolType, string? PoolLocation, bool? PoolIsSeasonal,
    bool? PoolIsHeated, decimal CleaningFee, decimal ExtraGuestFee, decimal ExtraBedFee, bool AllowsSmoking,
    bool AllowsParties, bool AllowsPets, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd,
    string? CustomRulesText, int CancellationFreeCancellationDays, bool CancellationDepositRefundable,
    string? CancellationCustomTermsText, decimal? DepositPercentage);

public sealed record AddRoomTypeRequest(string Name, decimal BasePricePerNight, int? Capacity);
public sealed record AddAccommodationUnitRequest(string Label);
public sealed record AddUnitBlockRequest(DateOnly From, DateOnly To);
public sealed record SetUnitAvailabilityRequest(bool IsActive);
