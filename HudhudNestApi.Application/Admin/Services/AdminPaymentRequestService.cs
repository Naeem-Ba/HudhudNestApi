using System.Text.Json;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Plans.Services;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Admin.Services;

public sealed class AdminPaymentRequestService : IAdminPaymentRequestService
{
    private readonly IAdminPaymentRequestQueryRepository _queries;
    private readonly IListingFeeRepository _fees;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly BillingNotifier _billing;
    private readonly ILogger<AdminPaymentRequestService> _logger;

    public AdminPaymentRequestService(
        IAdminPaymentRequestQueryRepository queries,
        IListingFeeRepository fees,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        BillingNotifier billing,
        ILogger<AdminPaymentRequestService> logger)
    {
        _queries = queries;
        _fees = fees;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _billing = billing;
        _logger = logger;
    }

    public async Task<PendingPaymentRequestsDto> GetPendingAsync(CancellationToken ct = default)
        => new(
            await _queries.GetPendingPlanRequestsAsync(ct),
            await _queries.GetPendingFeaturedRequestsAsync(ct),
            await _queries.GetPendingExtensionRequestsAsync(ct));

    public Task<AdminOperationResult> RejectFeaturedAsync(
        Guid transactionId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
        => RejectFeeAsync(
            transactionId, TransactionType.FeaturedListingFee, AuditActions.FeaturedRequestRejectedByAdmin,
            "Featured request rejected.", "This transaction is not a featured-listing fee.",
            reason, performedByUserId, ipAddress, ct);

    public Task<AdminOperationResult> RejectExtensionAsync(
        Guid transactionId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
        => RejectFeeAsync(
            transactionId, TransactionType.ListingExtensionFee, AuditActions.ExtensionRequestRejectedByAdmin,
            "Extension request rejected.", "This transaction is not a listing-extension fee.",
            reason, performedByUserId, ipAddress, ct);

    private async Task<AdminOperationResult> RejectFeeAsync(
        Guid transactionId,
        TransactionType expectedType,
        string auditAction,
        string okMessage,
        string wrongTypeMessage,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct)
    {
        var fee = await _fees.GetByIdAsync(transactionId, ct);

        if (fee is null)
            return AdminOperationResult.NotFoundResult("Transaction was not found.");

        if (fee.TransactionType != expectedType)
            return AdminOperationResult.ConflictResult(wrongTypeMessage);

        // Same replay guard as the confirmation: only a Pending fee can be resolved, so a
        // rejection cannot undo a payment that was already confirmed and delivered.
        if (fee.Status != TransactionStatus.Pending)
            return AdminOperationResult.ConflictResult($"This request is already {fee.Status}.");

        fee.MarkFailed();
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditLogs.LogAsync(
            userId: performedByUserId,
            action: auditAction,
            ipAddress: ipAddress,
            oldValue: JsonSerializer.Serialize(new
            {
                transactionId = fee.Id,
                propertyId = fee.PropertyId,
                ownerId = fee.PayerId,
                status = nameof(TransactionStatus.Pending)
            }),
            newValue: JsonSerializer.Serialize(new
            {
                transactionId = fee.Id,
                status = fee.Status.ToString(),
                reason,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);

        _logger.LogInformation(
            "Listing fee request rejected. Type={Type}, TransactionId={TransactionId}, PropertyId={PropertyId}, RejectedBy={RejectedBy}",
            expectedType, fee.Id, fee.PropertyId, performedByUserId);

        var property = await _properties.GetByIdAsync(fee.PropertyId, ct);
        await _billing.ListingFeeRejectedAsync(
            fee.PayerId, fee.Id, expectedType == TransactionType.FeaturedListingFee, property?.Title ?? string.Empty, reason, ct);

        return AdminOperationResult.Ok(okMessage);
    }
}
