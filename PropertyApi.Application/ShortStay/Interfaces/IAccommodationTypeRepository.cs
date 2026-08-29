using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Interfaces;

/// <summary>Read-only — AccommodationType rows are seeded/admin-managed, not user-created.</summary>
public interface IAccommodationTypeRepository
{
    Task<AccommodationType?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AccommodationType>> GetActiveAsync(CancellationToken ct = default);
}
