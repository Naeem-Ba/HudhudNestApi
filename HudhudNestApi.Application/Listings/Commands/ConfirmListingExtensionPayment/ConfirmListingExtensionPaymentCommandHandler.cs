using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Listings.Commands.ConfirmListingExtensionPayment;

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
    private readonly IListingFeeRepository _extensions;
    private readonly IPropertyRepository _properties;
    private readonly IAgencyRepository _agencies;
    private readonly IListingQuotaGuard _quotaGuard;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly ILogger<ConfirmListingExtensionPaymentCommandHandler> _logger;

    public ConfirmListingExtensionPaymentCommandHandler(
        IListingFeeRepository extensions,
        IPropertyRepository properties,
        IAgencyRepository agencies,
        IListingQuotaGuard quotaGuard,
        IUnitOfWork unitOfWork,
        IPublisher publisher,
        ILogger<ConfirmListingExtensionPaymentCommandHandler> logger)
    {
        _extensions = extensions;
        _properties = properties;
        _agencies = agencies;
        _quotaGuard = quotaGuard;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
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

        // The fee was quoted while the listing could still be extended; it may have been sold, archived or
        // taken down since. Confirming would resurrect it (QA Phase 8, P8-D02), so refuse and leave the
        // fee Pending for the administrator to settle with the owner.
        if (!property.CanBeExtended)
        {
            throw new ConflictException(
                "لا يمكن تأكيد هذا التمديد: الإعلان مباع أو مؤجَّر أو مؤرشف أو موقوف من الإدارة. " +
                "عالج الرسوم مع المالك دون تمديد.");
        }

        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // Bringing an expired listing back adds one to the active count, which expiry had released.
            // Same advisory lock as creation, so a concurrent create cannot take the last seat between
            // the check and the extension (QA Phase 8, P8-D05).
            if (property.Status == PropertyStatus.Expired)
            {
                var owner = await _agencies.GetUserAccountAsync(property.OwnerId, cancellationToken)
                    ?? throw new ConflictException("تعذّر العثور على صاحب الإعلان.");

                var lockKey = owner.AgencyId is { } agencyId
                    ? ListingQuotaLock.ForAgency(agencyId)
                    : ListingQuotaLock.ForOwner(owner.Id);

                await _unitOfWork.AcquireAdvisoryLockAsync(lockKey, cancellationToken);
                await _quotaGuard.EnsureCapacityAsync(owner, cancellationToken);
            }

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

        // ExtendPublication republishes an expired/unpublished listing (see its own doc comment)
        // — the same "listing is live again" moment PATCH /publish raises for a fresh draft, so
        // SocialDistribution must hear about it too. Best-effort, same fire-and-notify contract as
        // PublishPropertyCommandHandler: the paid extension itself must never fail over a broken
        // subscriber.
        try
        {
            await _publisher.Publish(
                new PropertyPublishedEvent(property.Id, property.PublishedAt ?? DateTime.UtcNow, property.OwnerId),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish PropertyPublishedEvent after a paid listing extension. PropertyId={PropertyId}", property.Id);
        }

        return property.ExpiresAt!.Value;
    }
}
