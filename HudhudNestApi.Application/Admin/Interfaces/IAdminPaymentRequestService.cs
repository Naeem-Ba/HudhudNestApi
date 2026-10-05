using HudhudNestApi.Application.Admin.DTOs;

namespace HudhudNestApi.Application.Admin.Interfaces;

/// <summary>
/// The administrator's queue of things users asked for and must pay for: paid plans and
/// featured placement. Nothing here grants anything — approving a plan goes through
/// IAdminSubscriptionService, confirming a featured fee through
/// ConfirmFeaturedListingPaymentCommand (the one place a listing is promoted). This service
/// only lists what is waiting and declines a featured request.
/// </summary>
public interface IAdminPaymentRequestService
{
    Task<PendingPaymentRequestsDto> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Marks a Pending featured fee Failed (payment not received). The listing is untouched.</summary>
    Task<AdminOperationResult> RejectFeaturedAsync(
        Guid transactionId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);
}
