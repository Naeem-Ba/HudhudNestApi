using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace PropertyApi.Application.Common.Interfaces;

/// <summary>
/// Minimal read-only property accessor.
/// Keeps booking and review handlers independent of the full property repository.
/// </summary>
public interface IPropertyReadRepository
{
    Task<PropertySummary?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Lightweight DTO returned by IPropertyReadRepository.
/// </summary>
/// <param name="EstimatedValue">
/// PurchasePrice for a for-sale listing, ColdRent otherwise — used where a single indicative
/// property value figure is needed (e.g. Investment Discovery's financial overview). Not a
/// valuation; just whichever price the owner already entered.
/// </param>
public sealed record PropertySummary(
    Guid Id,
    string Title,
    string City,
    Guid OwnerId,
    bool IsPublished,
    string? MainImageUrl,
    string? CountryCode = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    decimal? EstimatedValue = null,
    string? CurrencyCode = null
);
