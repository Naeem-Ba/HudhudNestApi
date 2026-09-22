using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceRequestRepository
{
    Task AddAsync(ServiceRequest request, CancellationToken ct = default);

    /// <summary>Includes Property/Requester/ServiceProvider/ServiceOffering navigations —
    /// callers should not need a second round-trip to build a ServiceRequestDto.</summary>
    Task<ServiceRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceRequest>> GetByRequesterIdAsync(
        Guid requesterId, CancellationToken ct = default);

    /// <summary>Provider inbox — takes ServiceProviderId (not UserId); the caller resolves
    /// "which provider is this user" first.</summary>
    Task<IReadOnlyList<ServiceRequest>> GetByServiceProviderIdAsync(
        Guid serviceProviderId, CancellationToken ct = default);

    /// <summary>True if the requester already has a live (not terminal) request for this
    /// property+offering pair — mirrors IVisitRepository.HasPendingVisitAsync, prevents a
    /// user from spamming duplicate requests for the same service.</summary>
    Task<bool> HasActiveRequestAsync(
        Guid propertyId, Guid requesterId, Guid serviceOfferingId, CancellationToken ct = default);
}
