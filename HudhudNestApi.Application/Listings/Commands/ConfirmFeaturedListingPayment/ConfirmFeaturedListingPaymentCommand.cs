using MediatR;

namespace HudhudNestApi.Application.Listings.Commands.ConfirmFeaturedListingPayment;

/// <summary>
/// Settles a pending featured-placement fee and promotes the listing.
/// Administrative: the caller is whoever verified the money arrived.
/// Returns the moment the placement now runs until.
/// </summary>
public sealed record ConfirmFeaturedListingPaymentCommand(
    Guid TransactionId,
    Guid ConfirmingUserId) : IRequest<DateTime>;
