using PropertyApi.Domain.Bookings.Entities;

namespace PropertyApi.Application.Bookings.Interfaces;

public interface IVisitRepository
{
    Task AddAsync(VisitRequest visit, CancellationToken ct = default);

    Task<VisitRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> HasPendingVisitAsync(
        Guid propertyId,
        Guid requesterId,
        CancellationToken ct = default);

    Task<IReadOnlyList<VisitRequest>> GetByRequesterIdAsync(
        Guid requesterId,
        CancellationToken ct = default);

    Task<IReadOnlyList<VisitRequest>> GetByPropertyIdAsync(
        Guid propertyId,
        CancellationToken ct = default);
}
