using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
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

    public GetShortStayListingByIdQueryHandler(
        IShortStayListingRepository listings, IAccommodationTypeRepository accommodationTypes)
    {
        _listings = listings;
        _accommodationTypes = accommodationTypes;
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

        return isOwner ? listing.ToDto(accommodationType) : listing.ToPublicDto(accommodationType);
    }
}
