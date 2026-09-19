using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.Interfaces;

namespace PropertyApi.Application.ShortStay.Commands.SetShortStayListingAmenities;

/// <summary>
/// Replaces the listing's full amenity set — reuses the shared Amenity lookup table already
/// serving traditional Property listings (GET /api/lookups/amenities), no duplicate amenity
/// catalog for Short-Stay. Was a real gap until now: the domain/DB relation
/// (ShortStayListingAmenity) existed since the initial migration, but no command or DTO field
/// ever exposed it — amenities were unreachable end-to-end despite being modeled.
/// </summary>
public sealed record SetShortStayListingAmenitiesCommand(
    Guid ListingId, Guid OwnerId, IReadOnlyList<Guid> AmenityIds) : IRequest<bool>;

public sealed class SetShortStayListingAmenitiesCommandHandler : IRequestHandler<SetShortStayListingAmenitiesCommand, bool>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IUnitOfWork _uow;

    public SetShortStayListingAmenitiesCommandHandler(IShortStayListingRepository listings, IUnitOfWork uow)
    {
        _listings = listings;
        _uow = uow;
    }

    public async Task<bool> Handle(SetShortStayListingAmenitiesCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can set amenities.");

        listing.SetAmenities(request.AmenityIds);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
