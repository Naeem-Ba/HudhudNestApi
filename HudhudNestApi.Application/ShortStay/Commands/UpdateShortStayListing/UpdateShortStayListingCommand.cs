using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Domain.ShortStay.ValueObjects;

namespace HudhudNestApi.Application.ShortStay.Commands.UpdateShortStayListing;

/// <summary>
/// Consolidated update covering every editable listing field in one round-trip — this
/// codebase groups related field updates into a handful of aggregate methods rather than one
/// endpoint per field (see ShortStayListing's own Update* method grouping), so the command
/// mirrors that grouping instead of exploding into a dozen near-identical single-field commands.
/// </summary>
public sealed record UpdateShortStayListingCommand(
    Guid ListingId,
    Guid OwnerId,
    string Title,
    string Description,
    int Capacity,
    int Bedrooms,
    int Bathrooms,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    bool SelfCheckInEnabled,
    bool InstantBookingEnabled,
    bool RequestBookingEnabled,
    decimal Latitude,
    decimal Longitude,
    int? GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    string? City,
    string LocationVisibility,
    string? PoolType,
    string? PoolLocation,
    bool? PoolIsSeasonal,
    bool? PoolIsHeated,
    decimal CleaningFee,
    decimal ExtraGuestFee,
    decimal ExtraBedFee,
    bool AllowsSmoking,
    bool AllowsParties,
    bool AllowsPets,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string? CustomRulesText,
    int CancellationFreeCancellationDays,
    bool CancellationDepositRefundable,
    string? CancellationCustomTermsText,
    decimal? DepositPercentage) : IRequest<ShortStayListingDto>;

public sealed class UpdateShortStayListingCommandHandler
    : IRequestHandler<UpdateShortStayListingCommand, ShortStayListingDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IAccommodationTypeRepository _accommodationTypes;
    private readonly IUnitOfWork _uow;

    public UpdateShortStayListingCommandHandler(
        IShortStayListingRepository listings,
        IAccommodationTypeRepository accommodationTypes,
        IUnitOfWork uow)
    {
        _listings = listings;
        _accommodationTypes = accommodationTypes;
        _uow = uow;
    }

    public async Task<ShortStayListingDto> Handle(UpdateShortStayListingCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can update it.");

        listing.UpdateBasicInfo(request.Title, request.Description, request.Capacity, request.Bedrooms,
            request.Bathrooms, request.CheckInTime, request.CheckOutTime, request.SelfCheckInEnabled);

        listing.UpdateBookingSettings(request.InstantBookingEnabled, request.RequestBookingEnabled);

        var locationVisibility = Enum.Parse<LocationVisibility>(request.LocationVisibility, ignoreCase: true);
        listing.UpdateLocation(request.Latitude, request.Longitude, request.GovernorateId, request.DistrictId,
            request.NeighborhoodId, request.City, locationVisibility);

        if (request.PoolType is not null && request.PoolLocation is not null)
        {
            var pool = PoolDetails.Create(
                Enum.Parse<PoolType>(request.PoolType, ignoreCase: true),
                Enum.Parse<PoolLocation>(request.PoolLocation, ignoreCase: true),
                request.PoolIsSeasonal ?? false,
                request.PoolIsHeated ?? false);
            listing.UpdatePoolDetails(pool);
        }
        else
        {
            listing.UpdatePoolDetails(null);
        }

        listing.UpdateFees(request.CleaningFee, request.ExtraGuestFee, request.ExtraBedFee);
        listing.UpdateHouseRules(request.AllowsSmoking, request.AllowsParties, request.AllowsPets,
            request.QuietHoursStart, request.QuietHoursEnd, request.CustomRulesText);
        listing.UpdateCancellationPolicy(request.CancellationFreeCancellationDays,
            request.CancellationDepositRefundable, request.CancellationCustomTermsText);
        listing.UpdateDepositPolicy(request.DepositPercentage);

        _listings.Update(listing);
        await _uow.SaveChangesAsync(ct);

        var accommodationType = await _accommodationTypes.GetByIdAsync(listing.AccommodationTypeId, ct)
            ?? throw new NotFoundException("AccommodationType was not found.");

        return listing.ToDto(accommodationType);
    }
}
