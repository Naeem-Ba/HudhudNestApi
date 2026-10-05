using HudhudNestApi.Domain.Plans.Entities;

namespace HudhudNestApi.Application.Plans.Interfaces;

public interface IPlanRequestRepository
{
    /// <summary>The user's single outstanding (Pending) request, tracked, or null.</summary>
    Task<PlanRequest?> GetPendingForUserAsync(Guid userId, CancellationToken ct = default);

    Task<PlanRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(PlanRequest request, CancellationToken ct = default);

    /// <summary>Re-reads the row from the database, discarding what the context tracked — used under the user lock.</summary>
    Task ReloadAsync(PlanRequest request, CancellationToken ct = default);
}
