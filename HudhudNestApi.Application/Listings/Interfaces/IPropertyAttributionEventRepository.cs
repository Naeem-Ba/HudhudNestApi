using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Interfaces;

public interface IPropertyAttributionEventRepository
{
    void Add(PropertyAttributionEvent attributionEvent);

    /// <summary>Phase 14 spec §10 read side — total events of one type, optionally scoped to a property and/or date range (both UTC, inclusive). <paramref name="eventType"/> null counts every type together.</summary>
    Task<int> CountAsync(Guid? propertyId, PropertyAttributionEventType? eventType, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);

    /// <summary>Same scope as <see cref="CountAsync"/>, broken down by UtmSource — feeds the "visits/contacts/leads per platform" report rows.</summary>
    Task<IReadOnlyList<UtmSourceCount>> CountByUtmSourceAsync(Guid? propertyId, PropertyAttributionEventType? eventType, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
