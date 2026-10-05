using System.Text.Json;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Admin.Services;

public sealed class AdminPaymentRequestService : IAdminPaymentRequestService
{
    private readonly IAdminPaymentRequestQueryRepository _queries;
    private readonly IListingFeeRepository _fees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<AdminPaymentRequestService> _logger;

    public AdminPaymentRequestService(
        IAdminPaymentRequestQueryRepository queries,
        IListingFeeRepository fees,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        ILogger<AdminPaymentRequestService> logger)
    {
        _queries = queries;
        _fees = fees;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<PendingPaymentRequestsDto> GetPendingAsync(CancellationToken ct = default)
        => new(
            await _queries.GetPendingPlanRequestsAsync(ct),
            await _queries.GetPendingFeaturedRequestsAsync(ct));

    public async Task<AdminOperationResult> RejectFeaturedAsync(
        Guid transactionId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var fee = await _fees.GetByIdAsync(transactionId, ct);

        if (fee is null)
            return AdminOperationResult.NotFoundResult("Transaction was not found.");

        if (fee.TransactionType != TransactionType.FeaturedListingFee)
            return AdminOperationResult.ConflictResult("This transaction is not a featured-listing fee.");

        // Same replay guard as the confirmation: only a Pending fee can be resolved, so a
        // rejection cannot undo a payment that was already confirmed and delivered.
        if (fee.Status != TransactionStatus.Pending)
            return AdminOperationResult.ConflictResult($"This request is already {fee.Status}.");

        fee.MarkFailed();
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditLogs.LogAsync(
            userId: performedByUserId,
            action: AuditActions.FeaturedRequestRejectedByAdmin,
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
            "Featured request rejected. TransactionId={TransactionId}, PropertyId={PropertyId}, RejectedBy={RejectedBy}",
            fee.Id, fee.PropertyId, performedByUserId);

        return AdminOperationResult.Ok("Featured request rejected.");
    }
}
