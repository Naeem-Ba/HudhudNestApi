using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Admin.Interfaces;

/// <summary>
/// Stage 8 — the Valuation module's Admin Dashboard. Follows IAdminListingService's shape
/// (a plain admin-only Application service, not a MediatR command/query — same reasoning:
/// these are back-office actions/reads, not domain-triggering operations with their own
/// pipeline behaviors to run).
/// </summary>
public interface IAdminValuationInquiryService
{
    /// <summary>
    /// Every inquiry, optionally filtered by its real ValuationInquiryStatus (Pending,
    /// MatchedFromListings, AwaitingOfficeResponses, Completed, Expired), newest first.
    /// </summary>
    Task<PagedResult<AdminValuationInquirySummaryDto>> GetInquiriesAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken ct = default);

    /// <summary>
    /// Per-office invitation/response/SLA statistics — one row per agency that has ever
    /// received a valuation invitation, ordered by TotalInvitations descending (busiest
    /// offices first).
    /// </summary>
    Task<IReadOnlyList<AdminOfficeValuationStatisticsDto>> GetOfficeStatisticsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Marks an office for manual review (e.g. a poor SLA-compliance pattern the admin spotted
    /// on the statistics table above). Does not suspend or deactivate the agency — see
    /// Agency.FlagForManualReview's own doc comment.
    /// </summary>
    Task<AdminOperationResult> FlagOfficeForReviewAsync(
        Guid agencyId,
        string reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> ClearOfficeReviewFlagAsync(
        Guid agencyId,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);
}
