using MediatR;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;

namespace PropertyApi.Application.ShortStay.Queries.GetMyShortStayListings;

public sealed record GetMyShortStayListingsQuery(Guid OwnerId) : IRequest<IReadOnlyList<ShortStayListingSummaryDto>>;

public sealed class GetMyShortStayListingsQueryHandler
    : IRequestHandler<GetMyShortStayListingsQuery, IReadOnlyList<ShortStayListingSummaryDto>>
{
    private readonly IShortStayListingRepository _listings;

    public GetMyShortStayListingsQueryHandler(IShortStayListingRepository listings) => _listings = listings;

    public async Task<IReadOnlyList<ShortStayListingSummaryDto>> Handle(
        GetMyShortStayListingsQuery request, CancellationToken ct)
    {
        var listings = await _listings.GetByOwnerAsync(request.OwnerId, ct);

        return listings.Select(listing =>
        {
            var cheapestRoomType = listing.RoomTypes.Where(rt => rt.IsActive)
                .OrderBy(rt => rt.BasePricePerNight).FirstOrDefault();

            return new ShortStayListingSummaryDto(
                listing.Id, listing.Title, listing.AccommodationType.Code, listing.AccommodationType.NameAr,
                listing.Capacity, listing.Bedrooms, listing.Bathrooms, cheapestRoomType?.BasePricePerNight,
                listing.City, listing.Latitude, listing.Longitude, listing.IsPublished,
                listing.Photos.OrderBy(p => p.SortOrder).FirstOrDefault()?.Url, null, 0);
        }).ToList();
    }
}
