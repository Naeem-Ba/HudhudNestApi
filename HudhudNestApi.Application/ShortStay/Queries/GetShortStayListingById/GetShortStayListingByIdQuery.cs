using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.Views;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Mapping;

namespace HudhudNestApi.Application.ShortStay.Queries.GetShortStayListingById;

/// <summary>RequestingUserId is null for an anonymous caller — the owner always sees exact
/// location and unpublished listings; anyone else only sees a published listing, redacted
/// per LocationVisibility (see ShortStayListingMappingExtensions.ToPublicDto).</summary>
public sealed record GetShortStayListingByIdQuery(Guid ListingId, Guid? RequestingUserId) : IRequest<ShortStayListingDto>;

public sealed class GetShortStayListingByIdQueryHandler
    : IRequestHandler<GetShortStayListingByIdQuery, ShortStayListingDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IAccommodationTypeRepository _accommodationTypes;
    private readonly IListingViewRepository _views;

    public GetShortStayListingByIdQueryHandler(
        IShortStayListingRepository listings, IAccommodationTypeRepository accommodationTypes, IListingViewRepository views)
    {
        _listings = listings;
        _accommodationTypes = accommodationTypes;
        _views = views;
    }

    public async Task<ShortStayListingDto> Handle(GetShortStayListingByIdQuery request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        var isOwner = request.RequestingUserId.HasValue && listing.OwnerId == request.RequestingUserId.Value;

        if (!listing.IsPublished && !isOwner)
            throw new NotFoundException($"Listing {request.ListingId} was not found.");

        var accommodationType = await _accommodationTypes.GetByIdAsync(listing.AccommodationTypeId, ct)
            ?? throw new NotFoundException("AccommodationType was not found.");

        var dto = isOwner ? listing.ToDto(accommodationType) : listing.ToPublicDto(accommodationType);
        return await _views.WithViewCountAsync(dto, ct);
    }
}
