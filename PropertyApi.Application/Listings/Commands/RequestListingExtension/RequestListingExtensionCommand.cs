using MediatR;
using PropertyApi.Application.Listings.DTOs;

namespace PropertyApi.Application.Listings.Commands.RequestListingExtension;

/// <summary>
/// Owner asks to extend one listing by another publication period, for a fee.
/// Produces an outstanding charge; it does not extend anything on its own.
/// </summary>
public sealed record RequestListingExtensionCommand(
    Guid PropertyId,
    Guid RequestingUserId) : IRequest<ListingExtensionQuoteDto>;
