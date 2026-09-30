using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Listings.Commands.ConfirmFeaturedListingPayment;

/// <summary>
/// The one place a listing is actually promoted to featured placement.
///
/// Marking the fee Completed and promoting the listing are a single unit of work: if the
/// promotion failed after the fee was marked paid, the owner would have paid for nothing
/// and no retry could fix it, because the fee would no longer be Pending.
/// </summary>
public sealed class ConfirmFeaturedListingPaymentCommandHandler
    : IRequestHandler<ConfirmFeaturedListingPaymentCommand, DateTime>
{
    private readonly IListingFeeRepository _fees;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmFeaturedListingPaymentCommandHandler> _logger;

    public ConfirmFeaturedListingPaymentCommandHandler(
        IListingFeeRepository fees,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmFeaturedListingPaymentCommandHandler> logger)
    {
        _fees = fees;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<DateTime> Handle(
        ConfirmFeaturedListingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var fee = await _fees.GetByIdAsync(request.TransactionId, cancellationToken);

        if (fee is null)
            throw new NotFoundException("Transaction was not found.");

        // A confirmation aimed at the wrong fee type would grant the wrong thing for the
        // wrong price — an extension fee is $1, a featured fee is $5.
        if (fee.TransactionType != TransactionType.FeaturedListingFee)
        {
            throw new ConflictException(
                "هذه المعاملة ليست رسوم تمييز إعلان.");
        }

        // Replaying a confirmation must not grant a second placement for one payment.
        if (fee.Status != TransactionStatus.Pending)
        {
            throw new ConflictException(
                $"لا يمكن تأكيد معاملة حالتها {fee.Status}.");
        }

        var property = await _properties.GetByIdAsync(fee.PropertyId, cancellationToken);

        if (property is null || property.IsDeleted)
        {
            throw new ConflictException(
                "لم يعد الإعلان موجوداً — لا يمكن تمييزه.");
        }

        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            fee.MarkCompleted();

            // MarkFeatured rejects an expired or deleted listing. That throw is intentional
            // here rather than a silent skip: if the listing expired between the owner
            // paying and the administrator confirming, the money must not be taken for a
            // placement that cannot be delivered — the rollback below leaves the fee Pending
            // so it can be refunded or applied after the owner extends the listing.
            property.MarkFeatured(ListingLifecyclePolicy.FeaturedPeriod, now);

            _properties.Update(property);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "Featured placement granted. PropertyId={PropertyId}, TransactionId={TransactionId}, ConfirmedBy={ConfirmedBy}, FeaturedUntil={FeaturedUntil}",
            property.Id,
            fee.Id,
            request.ConfirmingUserId,
            property.FeaturedUntil);

        return property.FeaturedUntil!.Value;
    }
}
