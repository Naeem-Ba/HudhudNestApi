using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Transactions.Enums;

namespace PropertyApi.Application.Listings.Commands.ConfirmListingExtensionPayment;

/// <summary>
/// The one place a listing extension is actually granted.
///
/// Marking the fee Completed and extending the listing are a single unit of work: if the
/// listing extension failed after the fee was marked paid, the owner would have paid for
/// nothing and no retry would fix it, because the fee would no longer be Pending.
/// </summary>
public sealed class ConfirmListingExtensionPaymentCommandHandler
    : IRequestHandler<ConfirmListingExtensionPaymentCommand, DateTime>
{
    private readonly IListingExtensionRepository _extensions;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmListingExtensionPaymentCommandHandler> _logger;

    public ConfirmListingExtensionPaymentCommandHandler(
        IListingExtensionRepository extensions,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmListingExtensionPaymentCommandHandler> logger)
    {
        _extensions = extensions;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<DateTime> Handle(
        ConfirmListingExtensionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var fee = await _extensions.GetByIdAsync(request.TransactionId, cancellationToken);

        if (fee is null)
            throw new NotFoundException("Transaction was not found.");

        if (fee.TransactionType != TransactionType.ListingExtensionFee)
        {
            throw new ConflictException(
                "هذه المعاملة ليست رسوم تمديد إعلان.");
        }

        // Replaying a confirmation must not grant a second period for one payment.
        if (fee.Status != TransactionStatus.Pending)
        {
            throw new ConflictException(
                $"لا يمكن تأكيد معاملة حالتها {fee.Status}.");
        }

        // The extension has to reach a listing that is still recoverable. Once the sweep has
        // deleted it, IsDeleted is set and GetByIdAsync's global query filter hides it —
        // so "not found" here genuinely means "grace period already ran out".
        var property = await _properties.GetByIdAsync(fee.PropertyId, cancellationToken);

        if (property is null || property.IsDeleted)
        {
            throw new ConflictException(
                "لم يعد الإعلان قابلاً للاسترجاع — انقضت مهلة السماح قبل تأكيد الدفع.");
        }

        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            fee.MarkCompleted();
            property.ExtendPublication(ListingLifecyclePolicy.PublicationPeriod, now);

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
            "Listing extension granted. PropertyId={PropertyId}, TransactionId={TransactionId}, ConfirmedBy={ConfirmedBy}, NewExpiresAt={ExpiresAt}",
            property.Id,
            fee.Id,
            request.ConfirmingUserId,
            property.ExpiresAt);

        return property.ExpiresAt!.Value;
    }
}
