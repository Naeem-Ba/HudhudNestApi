using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Transactions.Entities;
using PropertyApi.Domain.Transactions.Enums;

namespace PropertyApi.Application.Listings.Commands.RequestFeaturedListing;

/// <summary>
/// Records the fee an owner owes to promote one listing, and returns it as a quote.
///
/// IMPORTANT — this deliberately does NOT promote the listing, for exactly the reason
/// RequestListingExtensionCommandHandler does not extend one: there is no payment gateway,
/// so the only honest thing a handler can do here is record a Pending charge and stop. If
/// it set IsFeatured here, featured placement would be free for anyone who called the
/// endpoint and never paid. Promotion is applied by
/// ConfirmFeaturedListingPaymentCommandHandler once the Transaction has actually settled —
/// today an administrator confirming an offline transfer, later a gateway callback.
///
/// IsFeatured and FeaturedUntil existed as dead columns before this: no code read or wrote
/// them. They are now reachable only through this path.
/// </summary>
public sealed class RequestFeaturedListingCommandHandler
    : IRequestHandler<RequestFeaturedListingCommand, FeaturedListingQuoteDto>
{
    /// <summary>
    /// Receiver of a platform fee. There is no platform UserAccount row, and
    /// Transaction.ReceiverId carries no foreign key, so Guid.Empty is the platform
    /// sentinel — the same one RequestListingExtensionCommandHandler uses.
    /// </summary>
    private static readonly Guid PlatformReceiverId = Guid.Empty;

    private readonly IPropertyRepository _properties;
    private readonly IListingFeeRepository _fees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RequestFeaturedListingCommandHandler> _logger;

    public RequestFeaturedListingCommandHandler(
        IPropertyRepository properties,
        IListingFeeRepository fees,
        IUnitOfWork unitOfWork,
        ILogger<RequestFeaturedListingCommandHandler> logger)
    {
        _properties = properties;
        _fees = fees;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<FeaturedListingQuoteDto> Handle(
        RequestFeaturedListingCommand request,
        CancellationToken cancellationToken)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, cancellationToken);

        if (property is null || property.IsDeleted)
            throw new NotFoundException("Property was not found.");

        if (property.OwnerId != request.RequestingUserId)
            throw new ForbiddenException("Only the listing owner can pay to feature it.");

        // Selling promotion for a listing nobody can find is selling nothing. An expired
        // listing is hidden from search, so the owner must extend it first.
        if (property.Status == PropertyStatus.Expired)
        {
            throw new ConflictException(
                "لا يمكن تمييز إعلان منتهٍ. مدّد الإعلان أولاً ثم ميّزه.");
        }

        if (!property.IsPublished)
        {
            throw new ConflictException(
                "لا يمكن تمييز إعلان غير منشور. انشر الإعلان أولاً ثم ميّزه.");
        }

        var now = DateTime.UtcNow;

        var existing = await _fees.GetPendingFeeAsync(
            property.Id,
            request.RequestingUserId,
            TransactionType.FeaturedListingFee,
            cancellationToken);

        if (existing is not null)
        {
            // Idempotent: tapping "feature this listing" repeatedly must not stack charges.
            return ToQuote(existing, property.FeaturedUntil, now, isExistingPendingFee: true);
        }

        var (currencyId, exchangeRate) = await _fees.GetUsdCurrencyAsync(cancellationToken);

        var fee = Transaction.Create(
            propertyId: property.Id,
            payerId: request.RequestingUserId,
            receiverId: PlatformReceiverId,
            transactionType: TransactionType.FeaturedListingFee,
            amount: ListingLifecyclePolicy.FeaturedListingFeeUsd,
            currencyId: currencyId,
            exchangeRateToUSD: exchangeRate,
            // No gateway exists, so nothing can settle Online yet. BankTransfer is the rail
            // an administrator actually confirms today.
            paymentMethod: TransactionPaymentMethod.BankTransfer);

        await _fees.AddAsync(fee, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Featured listing fee recorded as pending. PropertyId={PropertyId}, OwnerId={OwnerId}, TransactionId={TransactionId}, AmountUsd={Amount}",
            property.Id,
            request.RequestingUserId,
            fee.Id,
            ListingLifecyclePolicy.FeaturedListingFeeUsd);

        return ToQuote(fee, property.FeaturedUntil, now, isExistingPendingFee: false);
    }

    private static FeaturedListingQuoteDto ToQuote(
        Transaction fee,
        DateTime? currentFeaturedUntil,
        DateTime now,
        bool isExistingPendingFee)
    {
        // Mirrors Property.MarkFeatured: a purchase extends from the later of "now" and the
        // existing FeaturedUntil, so the projection here matches what settlement will do.
        var startsFrom = currentFeaturedUntil is { } until && until > now
            ? until
            : now;

        return new FeaturedListingQuoteDto(
            TransactionId: fee.Id,
            PropertyId: fee.PropertyId,
            AmountUsd: fee.AmountInUSD,
            FeaturedDays: (int)ListingLifecyclePolicy.FeaturedPeriod.TotalDays,
            CurrentFeaturedUntil: currentFeaturedUntil,
            ProjectedFeaturedUntil: startsFrom.Add(ListingLifecyclePolicy.FeaturedPeriod),
            IsExistingPendingFee: isExistingPendingFee);
    }
}
