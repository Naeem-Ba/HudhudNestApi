using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.ShortStay.Interfaces;

namespace PropertyApi.Application.ShortStay.Commands.PublishShortStayListing;

public sealed record PublishShortStayListingCommand(Guid ListingId, Guid OwnerId) : IRequest<bool>;

public sealed class PublishShortStayListingCommandHandler : IRequestHandler<PublishShortStayListingCommand, bool>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IUnitOfWork _uow;

    public PublishShortStayListingCommandHandler(IShortStayListingRepository listings, IUnitOfWork uow)
    {
        _listings = listings;
        _uow = uow;
    }

    public async Task<bool> Handle(PublishShortStayListingCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can publish it.");

        listing.Publish();
        _listings.Update(listing);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record UnpublishShortStayListingCommand(Guid ListingId, Guid OwnerId) : IRequest<bool>;

public sealed class UnpublishShortStayListingCommandHandler : IRequestHandler<UnpublishShortStayListingCommand, bool>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IUnitOfWork _uow;

    public UnpublishShortStayListingCommandHandler(IShortStayListingRepository listings, IUnitOfWork uow)
    {
        _listings = listings;
        _uow = uow;
    }

    public async Task<bool> Handle(UnpublishShortStayListingCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException($"Listing {request.ListingId} was not found.");

        if (listing.OwnerId != request.OwnerId)
            throw new ForbiddenException("Only the listing owner can unpublish it.");

        listing.Unpublish();
        _listings.Update(listing);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
