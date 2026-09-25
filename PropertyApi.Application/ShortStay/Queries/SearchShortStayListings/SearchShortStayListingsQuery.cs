using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;

namespace PropertyApi.Application.ShortStay.Queries.SearchShortStayListings;

public sealed record SearchShortStayListingsQuery(ShortStayListingSearchFilter Filter)
    : IRequest<PagedResult<ShortStayListingSummaryDto>>;

public sealed class SearchShortStayListingsQueryHandler
    : IRequestHandler<SearchShortStayListingsQuery, PagedResult<ShortStayListingSummaryDto>>
{
    private readonly IShortStayListingRepository _listings;

    public SearchShortStayListingsQueryHandler(IShortStayListingRepository listings) => _listings = listings;

    public async Task<PagedResult<ShortStayListingSummaryDto>> Handle(
        SearchShortStayListingsQuery request, CancellationToken ct)
    {
        var page = await _listings.SearchAsync(request.Filter, ct);

        var items = page.Items.Select(listing =>
        {
            var cheapestRoomType = listing.RoomTypes
                .Where(rt => rt.IsActive)
                .OrderBy(rt => rt.BasePricePerNight)
                .FirstOrDefault();

            return new ShortStayListingSummaryDto(
                Id: listing.Id,
                Title: listing.Title,
                AccommodationTypeCode: listing.AccommodationType.Code,
                AccommodationTypeNameAr: listing.AccommodationType.NameAr,
                Capacity: listing.Capacity,
                Bedrooms: listing.Bedrooms,
                Bathrooms: listing.Bathrooms,
                FromPricePerNight: cheapestRoomType?.BasePricePerNight,
                CurrencyCode: listing.CurrencyCode,
                City: listing.City,
                Latitude: listing.LocationVisibility == Domain.ShortStay.Enums.LocationVisibility.Exact
                    ? listing.Latitude
                    : Domain.ShortStay.Entities.ShortStayListing.ApproximateCoordinate(listing.Latitude),
                Longitude: listing.LocationVisibility == Domain.ShortStay.Enums.LocationVisibility.Exact
                    ? listing.Longitude
                    : Domain.ShortStay.Entities.ShortStayListing.ApproximateCoordinate(listing.Longitude),
                IsPublished: listing.IsPublished,
                MainPhotoUrl: listing.Photos.OrderBy(p => p.SortOrder).FirstOrDefault()?.Url,
                AverageRating: null, // wired once ShortStayReview aggregation is added (see plan follow-ups)
                ReviewCount: 0);
        }).ToList();

        return new PagedResult<ShortStayListingSummaryDto>
        {
            Items = items,
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }
}
