using MediatR;

namespace PropertyApi.Application.Listings.Commands.ConfirmListingExtensionPayment;

/// <summary>
/// Settles a pending listing-extension fee and grants the listing another publication
/// period. Administrator action today (offline payment confirmation); the same command is
/// what a payment-gateway callback would dispatch.
/// </summary>
public sealed record ConfirmListingExtensionPaymentCommand(
    Guid TransactionId,
    Guid ConfirmingUserId) : IRequest<DateTime>;
