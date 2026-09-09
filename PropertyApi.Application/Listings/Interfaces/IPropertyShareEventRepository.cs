using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyShareEventRepository
{
    void Add(PropertyShareEvent shareEvent);

    /// <summary>Phase 14 spec §10 read side — total share events, optionally scoped to one property and/or a date range (both UTC, inclusive).</summary>
    Task<int> CountAsync(Guid? propertyId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    /// <summary>Same scope as <see cref="CountAsync"/>, broken down by UtmSource — feeds the "shares per platform" report row.</summary>
    Task<IReadOnlyList<UtmSourceCount>> CountByUtmSourceAsync(Guid? propertyId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
