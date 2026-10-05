using HudhudNestApi.Application.Admin.DTOs;

namespace HudhudNestApi.Application.Admin.Interfaces;

public interface IAdminPaymentRequestQueryRepository
{
    /// <summary>Pending paid-plan requests, oldest first (first come, first served).</summary>
    Task<IReadOnlyList<PendingPlanRequestDto>> GetPendingPlanRequestsAsync(CancellationToken ct = default);

    /// <summary>Pending featured-placement fees, oldest first.</summary>
    Task<IReadOnlyList<PendingFeaturedRequestDto>> GetPendingFeaturedRequestsAsync(CancellationToken ct = default);
}
