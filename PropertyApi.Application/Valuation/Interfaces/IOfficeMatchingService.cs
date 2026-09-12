using PropertyApi.Application.Valuation.DTOs;

namespace PropertyApi.Application.Valuation.Interfaces;

/// <summary>
/// Stage 4 — progressive geographic office matching for a <see cref="Domain.Valuation.Entities.ValuationInquiry"/>
/// that did not get enough Fast Path comparables (Stage 3's <c>RequiresOfficeValuation</c>).
///
/// Not a MediatR Query/Handler: this is a plain orchestration service, the same shape as
/// PropertyApi.Application.Listings.Services.PropertyOwnershipService — registered directly
/// in DependencyInjection.cs rather than picked up by assembly scanning.
///
/// Takes the inquiry's location fields directly rather than an InquiryId to load, for the
/// same reason Stage 3's GetComparableListingsQuery does: ValuationInquiry is still
/// Domain-only (Phase 2 deliberately left it unpersisted — no repository, no DbContext, no
/// migration exists for it yet), so there is nothing here could load an Inquiry by id from.
/// </summary>
public interface IOfficeMatchingService
{
    /// <param name="utcNow">
    /// Passed explicitly rather than read from the system clock inside the service — the
    /// same convention Agency/AgencyInvitation/ServiceRequest/PropertyShareEvent use, chosen
    /// there (and here) for deterministic, time-mocked unit tests.
    /// </param>
    Task<OfficeMatchingResult> MatchOfficesAsync(
        Guid inquiryId,
        int governorateId,
        int? districtId,
        int? neighborhoodId,
        DateTime utcNow,
        CancellationToken ct = default);
}
