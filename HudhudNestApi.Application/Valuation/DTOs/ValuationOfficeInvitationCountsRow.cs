namespace HudhudNestApi.Application.Valuation.DTOs;

/// <summary>
/// One agency's raw invitation counts — the database-side GROUP BY result behind
/// IValuationOfficeInvitationRepository.GetInvitationCountsByAgencyAsync (Stage 8, Admin
/// Dashboard). Deliberately just counts, no rate math here: turning these into
/// ResponseRate/SlaComplianceRate percentages is Application-layer arithmetic
/// (AdminValuationInquiryService), not something to compute inside the SQL projection.
/// </summary>
public sealed record ValuationOfficeInvitationCountsRow(
    Guid AgencyId,
    int TotalInvitations,
    int TotalResponses);
