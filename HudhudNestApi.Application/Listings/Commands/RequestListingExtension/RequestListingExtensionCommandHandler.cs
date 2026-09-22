using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Transactions.Entities;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Listings.Commands.RequestListingExtension;

/// <summary>
/// Records the fee an owner owes to extend one listing, and returns it as a quote.
///
/// IMPORTANT — this deliberately does NOT extend the listing.
/// There is no payment gateway in this system. The only honest thing a handler can do at
/// this point is record a Pending charge and stop. If it extended the listing here, the
/// fee would be permanently optional: every owner would get a free extension by calling
/// the endpoint and never paying. The extension is applied by
/// ConfirmListingExtensionPaymentCommandHandler, once something has actually settled the
/// Transaction — today an administrator confirming an offline payment, later a gateway
/// callback. That is the one seam a gateway needs to plug into.
/// </summary>
public sealed class RequestListingExtensionCommandHandler
    : IRequestHandler<RequestListingExtensionCommand, ListingExtensionQuoteDto>
{
    /// <summary>
    /// Receiver of a platform fee. There is no platform UserAccount row, and
    /// Transaction.ReceiverId carries no foreign key (see TransactionConfiguration —
    /// it is indexed, not constrained), so Guid.Empty is used as the platform sentinel.
    /// If a real platform account is ever introduced, this is the only line to change.
    /// </summary>
    private static readonly Guid PlatformReceiverId = Guid.Empty;

    private readonly IPropertyRepository _properties;
    private readonly IListingFeeRepository _extensions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RequestListingExtensionCommandHandler> _logger;

    public RequestListingExtensionCommandHandler(
        IPropertyRepository properties,
        IListingFeeRepository extensions,
        IUnitOfWork unitOfWork,
        ILogger<RequestListingExtensionCommandHandler> logger)
    {
        _properties = properties;
        _extensions = extensions;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ListingExtensionQuoteDto> Handle(
        RequestListingExtensionCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, cancellationToken);

        if (property is null || property.IsDeleted)
            throw new NotFoundException("Property was not found.");

        if (property.OwnerId != request.RequestingUserId)
            throw new ForbiddenException("Only the listing owner can pay to extend it.");

        var now = DateTime.UtcNow;

        // Paying early is allowed — an owner who sees the "expiring soon" notice should not
        // have to wait for their listing to go dark before they can act on it.
        var isExpired = property.Status == PropertyStatus.Expired;
        var isNearingExpiry = property.ExpiresAt is not null &&
            property.ExpiresAt <= now.Add(ListingLifecyclePolicy.ExpiryWarningLeadTime);

        if (!isExpired && !isNearingExpiry)
        {
            throw new ConflictException(
                "هذا الإعلان ما زال ضمن مدة نشره ولا يحتاج تمديداً بعد.");
        }

        // Past the grace window the listing is already deleted by the sweep, so the guard
        // above (IsDeleted) normally catches it. This is the belt to that braces: it keeps
        // the API from taking money for a listing that cannot come back.
        if (isExpired && property.ExpiresAt is not null &&
            now > ListingLifecyclePolicy.DeletionDueAt(property.ExpiresAt.Value))
        {
            throw new ConflictException(
                "انتهت مهلة استرجاع هذا الإعلان ولم يعد التمديد ممكناً.");
        }

        var existing = await _extensions.GetPendingFeeAsync(
            property.Id,
            request.RequestingUserId,
            TransactionType.ListingExtensionFee,
            cancellationToken);

        if (existing is not null)
        {
            // Idempotent: tapping "extend" repeatedly must not stack charges.
            return ToQuote(existing, property.ExpiresAt, isExpired, isExistingPendingFee: true);
        }

        var (currencyId, exchangeRate) = await _extensions.GetUsdCurrencyAsync(cancellationToken);

        var fee = Transaction.Create(
            propertyId: property.Id,
            payerId: request.RequestingUserId,
            receiverId: PlatformReceiverId,
            transactionType: TransactionType.ListingExtensionFee,
            amount: ListingLifecyclePolicy.SingleListingExtensionFeeUsd,
            currencyId: currencyId,
            exchangeRateToUSD: exchangeRate,
            // No gateway exists, so nothing can be settled Online yet. BankTransfer is the
            // rail an administrator actually confirms today; a gateway would create these
            // as Online instead.
            paymentMethod: TransactionPaymentMethod.BankTransfer);

        await _extensions.AddAsync(fee, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Listing extension fee recorded as pending. PropertyId={PropertyId}, OwnerId={OwnerId}, TransactionId={TransactionId}, AmountUsd={Amount}",
            property.Id,
            request.RequestingUserId,
            fee.Id,
            ListingLifecyclePolicy.SingleListingExtensionFeeUsd);

        return ToQuote(fee, property.ExpiresAt, isExpired, isExistingPendingFee: false);
    }

    private static ListingExtensionQuoteDto ToQuote(
        Transaction fee,
        DateTime? expiresAt,
        bool isExpired,
        bool isExistingPendingFee)
        => new(
            TransactionId: fee.Id,
            PropertyId: fee.PropertyId,
            AmountUsd: fee.AmountInUSD,
            ExtensionDays: (int)ListingLifecyclePolicy.PublicationPeriod.TotalDays,
            ExpiresAt: expiresAt,
            GraceEndsAt: isExpired && expiresAt is not null
                ? ListingLifecyclePolicy.DeletionDueAt(expiresAt.Value)
                : null,
            IsExistingPendingFee: isExistingPendingFee);
}
