using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Commands.RequestFeaturedListing;

/// <summary>
/// Owner asks to promote one listing to featured placement, for a fee.
/// Produces an outstanding charge; it does not promote anything on its own.
/// </summary>
public sealed record RequestFeaturedListingCommand(
    Guid PropertyId,
    Guid RequestingUserId) : IRequest<FeaturedListingQuoteDto>;
