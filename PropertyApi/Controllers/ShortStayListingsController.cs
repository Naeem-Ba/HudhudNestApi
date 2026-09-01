using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.ShortStay.Commands.AddAccommodationUnit;
using PropertyApi.Application.ShortStay.Commands.AddRoomType;
using PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;
using PropertyApi.Application.ShortStay.Commands.PublishShortStayListing;
using PropertyApi.Application.ShortStay.Commands.SetMinimumStayRules;
using PropertyApi.Application.ShortStay.Commands.SetPricingRules;
using PropertyApi.Application.ShortStay.Commands.UpdateShortStayListing;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Queries.GetMyShortStayListings;
using PropertyApi.Application.ShortStay.Queries.GetShortStayListingById;
using PropertyApi.Application.ShortStay.Queries.SearchShortStayListings;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/short-stay/listings")]
public sealed class ShortStayListingsController : ControllerBase
{
    private readonly ISender _mediator;
    public ShortStayListingsController(ISender mediator) => _mediator = mediator;

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
    public async Task<IActionResult> Create([FromBody] CreateShortStayListingRequest dto, CancellationToken ct)
    {
        var command = new CreateShortStayListingCommand(
            GetUserId(), dto.AccommodationTypeId, dto.Title, dto.Description, dto.Capacity, dto.Bedrooms,
            dto.Bathrooms, dto.CheckInTime, dto.CheckOutTime, dto.Latitude, dto.Longitude,
            dto.DefaultBasePricePerNight, dto.PropertyId);

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ShortStayListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
}

public sealed record CreateShortStayListingRequest(
    int AccommodationTypeId, string Title, string Description, int Capacity, int Bedrooms, int Bathrooms,
    TimeOnly CheckInTime, TimeOnly CheckOutTime, decimal Latitude, decimal Longitude,
    decimal DefaultBasePricePerNight, Guid? PropertyId);

public sealed record UpdateShortStayListingRequest(
    string Title, string Description, int Capacity, int Bedrooms, int Bathrooms, TimeOnly CheckInTime,
    TimeOnly CheckOutTime, bool SelfCheckInEnabled, bool InstantBookingEnabled, bool RequestBookingEnabled,
    decimal Latitude, decimal Longitude, int? GovernorateId, int? DistrictId, int? NeighborhoodId,
    string? City, string LocationVisibility, string? PoolType, string? PoolLocation, bool? PoolIsSeasonal,
    bool? PoolIsHeated, decimal CleaningFee, decimal ExtraGuestFee, decimal ExtraBedFee, bool AllowsSmoking,
    bool AllowsParties, bool AllowsPets, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd,
    string? CustomRulesText, int CancellationFreeCancellationDays, bool CancellationDepositRefundable,
    string? CancellationCustomTermsText, decimal? DepositPercentage);

public sealed record AddRoomTypeRequest(string Name, decimal BasePricePerNight, int? Capacity);
public sealed record AddAccommodationUnitRequest(string Label);
