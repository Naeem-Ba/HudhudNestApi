using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Interfaces;

/// <summary>
/// Append-only repository for PropertyPriceHistory rows.
/// Kept as its own small interface (rather than folded into IPropertyRepository).
/// </summary>
public interface IPropertyPriceHistoryRepository
{
    Task AddAsync(PropertyPriceHistory entry, CancellationToken ct = default);

    /// <summary>Newest first, at most <paramref name="take"/> rows, read-only.</summary>
    Task<IReadOnlyList<PropertyPriceHistory>> GetLatestAsync(Guid propertyId, int take, CancellationToken ct = default);
}
