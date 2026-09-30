using MediatR;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Valuation.Queries.GetComparableListings;

/// <summary>
/// Valuation Fast Path (Phase 3): looks up currently-listed comparable Properties for a
/// ValuationInquiry and computes a Preliminary Valuation from them (Min/Max/Average/Median),
/// or signals that Stage 4 (office valuation) is needed when too few — or none — exist.
///
/// Takes the inquiry's relevant field VALUES directly rather than an InquiryId to load —
/// ValuationInquiry (Phase 2) is Domain-only with no persistence yet (no DbSet, no
/// repository, no migration), so there is nowhere to load one FROM in this phase. InquiryId
/// is carried here purely for correlation in the result/logs, exactly like the "at least"
/// wording in this phase's own spec implies; the actual matching fields are the caller's
/// (a future CreateValuationInquiry-style orchestrator's) responsibility to supply from the
/// inquiry it already holds in memory.
/// </summary>
public sealed record GetComparableListingsQuery(
    Guid InquiryId,
    int? PropertyTypeId,
    decimal? Area,
    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    ListingType ListingType
) : IRequest<ComparableListingsResult>;
