using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Interfaces;

/// <summary>
/// Append-only repository for PropertyPriceHistory rows.
/// Kept as its own tiny interface (rather than folded into IPropertyRepository)
/// because it has a single write-only operation and no query methods yet —
/// growing it in place is easy once a "price timeline" read endpoint is needed.
/// </summary>
public interface IPropertyPriceHistoryRepository
{
    Task AddAsync(PropertyPriceHistory entry, CancellationToken ct = default);
}
