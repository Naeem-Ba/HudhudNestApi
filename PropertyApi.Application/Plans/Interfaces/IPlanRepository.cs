using PropertyApi.Domain.Plans.Entities;

namespace PropertyApi.Application.Plans.Interfaces;

public interface IPlanRepository
{
    Task<IReadOnlyList<Plan>> GetActiveAsync(CancellationToken ct = default);

    Task<Plan?> GetByTierAsync(string tier, CancellationToken ct = default);

    /// <summary>For resolving a UserAccount.PlanId back to a display tier — e.g. in
    /// GetCurrentUserQueryHandler. Deliberately not filtered by IsActive: a user whose
    /// plan was later deactivated should still see what they picked.</summary>
    Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
