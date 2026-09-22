namespace HudhudNestApi.Application.Admin.DTOs;

/// <summary>
/// One office's row on the Stage 8 Admin Dashboard's office-statistics table.
///
/// ResponseRate and SlaComplianceRate are deliberately both expressed as a ratio in [0, 1]
/// (not a raw count re-labeled as a "rate") — see AdminValuationInquiryService's own doc
/// comment for exactly how each is computed and why, under this module's current rules,
/// they end up numerically equal for every office today.
/// </summary>
public sealed class AdminOfficeValuationStatisticsDto
{
    public Guid AgencyId { get; init; }
    public string AgencyName { get; init; } = string.Empty;
    public int TotalInvitations { get; init; }
    public int TotalResponses { get; init; }
    public int ResponsesWithinSla { get; init; }
    public decimal ResponseRate { get; init; }
    public decimal SlaComplianceRate { get; init; }
    public bool RequiresManualReview { get; init; }
    public string? ManualReviewReason { get; init; }
    public DateTime? ManualReviewFlaggedAt { get; init; }
}
