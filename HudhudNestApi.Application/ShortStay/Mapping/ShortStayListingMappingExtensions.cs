using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Application.ShortStay.Mapping;

public static class ShortStayListingMappingExtensions
{
    public static RoomTypeDto ToDto(this RoomType roomType) => new(
        Id: roomType.Id,
        Name: roomType.Name,
        BasePricePerNight: roomType.BasePricePerNight,
        Capacity: roomType.Capacity,
        IsActive: roomType.IsActive,
        Units: roomType.Units.Select(u => new AccommodationUnitDto(u.Id, u.Label, u.IsActive)).ToList(),
        PricingRules: roomType.PricingRules.Select(r => new PricingRuleDto(
            r.Id, r.RuleType.ToString(), r.DayOfWeek, r.DateRangeStart, r.DateRangeEnd, r.PricePerNight)).ToList(),
        MinimumStayRules: roomType.MinimumStayRules.Select(r => new MinimumStayRuleDto(
            r.Id, r.MinimumNights, r.DateRangeStart, r.DateRangeEnd)).ToList());

    /// <summary>
    /// Maps the full aggregate for the OWNER's own view — exact Latitude/Longitude is always
    /// returned here because the caller has already been authorized as the owner by the
    /// handler. Use <see cref="ToPublicDto"/> for any anonymous/guest-facing read instead.
    /// </summary>
    public static ShortStayListingDto ToDto(this ShortStayListing listing, AccommodationType accommodationType) =>
        BuildDto(listing, accommodationType, exposeExactLocation: true);

    /// <summary>
    /// Maps for a non-owner viewer — enforces LocationVisibility.Approximate server-side by
    /// snapping Latitude/Longitude to a ~1 km grid (guests see the area, not the address) rather
    /// than relying on the frontend to hide them (spec §6: the exact location must stay
    /// unavailable to unauthorized viewers at the API level).
    /// </summary>
    public static ShortStayListingDto ToPublicDto(this ShortStayListing listing, AccommodationType accommodationType) =>
        BuildDto(listing, accommodationType, exposeExactLocation: listing.LocationVisibility == LocationVisibility.Exact);

    private static ShortStayListingDto BuildDto(ShortStayListing listing, AccommodationType accommodationType, bool exposeExactLocation) => new(
        Id: listing.Id,
        OwnerId: listing.OwnerId,
        PropertyId: listing.PropertyId,
        AccommodationTypeId: listing.AccommodationTypeId,
        AccommodationTypeCode: accommodationType.Code,
        CurrencyCode: listing.CurrencyCode,
        Title: listing.Title,
        Description: listing.Description,
        Capacity: listing.Capacity,
        Bedrooms: listing.Bedrooms,
        Bathrooms: listing.Bathrooms,
        CheckInTime: listing.CheckInTime,
        CheckOutTime: listing.CheckOutTime,
        SelfCheckInEnabled: listing.SelfCheckInEnabled,
        InstantBookingEnabled: listing.InstantBookingEnabled,
        RequestBookingEnabled: listing.RequestBookingEnabled,
        Latitude: exposeExactLocation ? listing.Latitude : ShortStayListing.ApproximateCoordinate(listing.Latitude),
        Longitude: exposeExactLocation ? listing.Longitude : ShortStayListing.ApproximateCoordinate(listing.Longitude),
        GovernorateId: listing.GovernorateId,
        DistrictId: listing.DistrictId,
        NeighborhoodId: listing.NeighborhoodId,
        City: listing.City,
        LocationVisibility: listing.LocationVisibility.ToString(),
        PoolType: listing.PoolDetails?.Type.ToString(),
        PoolLocation: listing.PoolDetails?.Location.ToString(),
        PoolIsSeasonal: listing.PoolDetails?.IsSeasonal,
        PoolIsHeated: listing.PoolDetails?.IsHeated,
        CleaningFee: listing.CleaningFee,
        ExtraGuestFee: listing.ExtraGuestFee,
        ExtraBedFee: listing.ExtraBedFee,
        AllowsSmoking: listing.AllowsSmoking,
        AllowsParties: listing.AllowsParties,
        AllowsPets: listing.AllowsPets,
        QuietHoursStart: listing.QuietHoursStart,
        QuietHoursEnd: listing.QuietHoursEnd,
        CustomRulesText: listing.CustomRulesText,
        CancellationFreeCancellationDays: listing.CancellationFreeCancellationDays,
        CancellationDepositRefundable: listing.CancellationDepositRefundable,
        CancellationCustomTermsText: listing.CancellationCustomTermsText,
        DepositPercentage: listing.DepositPercentage,
        IsPublished: listing.IsPublished,
        PublishedAt: listing.PublishedAt,
        RoomTypes: listing.RoomTypes.Select(rt => rt.ToDto()).ToList(),
        PhotoUrls: listing.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).ToList(),
        AmenityIds: listing.ListingAmenities.Select(a => a.AmenityId).ToList());
}
