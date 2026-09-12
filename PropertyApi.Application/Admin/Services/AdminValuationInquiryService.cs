using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Admin.Services;

/// <summary>
/// Stage 8 — the Valuation module's Admin Dashboard. Structurally mirrors AdminListingService
/// (begin tx, mutate the domain entity, save, commit, audit-log) for the one mutating action
/// this service has (flag/unflag an office); the two read methods are plain database-side
/// aggregation, same reasoning AdminUserQueryRepository's pagination already applies.
///
/// A note on ResponseRate vs SlaComplianceRate (the spec's own explicit warning: "Response
/// Rate must not just be a response count — measure response within the deadline"): both are
/// computed here as independent ratios —
///   ResponseRate       = TotalResponses     / TotalInvitations
///   SlaComplianceRate  = ResponsesWithinSla / TotalInvitations
/// — rather than SlaComplianceRate being silently aliased to ResponseRate. Under this module's
/// current write-path rule (SubmitOfficeResponseCommandHandler rejects any submission once its
/// invitation is past the parent inquiry's ExpiresAt — see that handler and
/// ValuationOfficeInvitation.IsExpired), a response can never be persisted late in the first
/// place, so ResponsesWithinSla == TotalResponses and the two rates come out numerically equal
/// for every office today. That is a real, current fact about this domain's rules, not a bug
/// in this service — TotalResponses/ResponsesWithinSla are still computed independently (the
/// latter via an explicit SubmittedAt &lt;= inquiry.ExpiresAt check in
/// IValuationOfficeResponseRepository.GetWithinSlaResponseCountsByAgencyAsync, not copied from
/// the former) so the two figures stay correctly distinct if that write-path rule ever loosens
/// (e.g. a future grace period or admin override that allows a late response to be recorded).
/// </summary>
public sealed class AdminValuationInquiryService : IAdminValuationInquiryService
{
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IValuationOfficeResponseRepository _responses;
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<AdminValuationInquiryService> _logger;

    public AdminValuationInquiryService(
        IValuationInquiryRepository inquiries,
        IValuationOfficeInvitationRepository invitations,
        IValuationOfficeResponseRepository responses,
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        ILogger<AdminValuationInquiryService> logger)
    {
        _inquiries = inquiries;
        _invitations = invitations;
        _responses = responses;
        _agencies = agencies;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<PagedResult<AdminValuationInquirySummaryDto>> GetInquiriesAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        ValuationInquiryStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<ValuationInquiryStatus>(status, ignoreCase: true, out var s))
        {
            parsedStatus = s;
        }

        var result = await _inquiries.GetPagedAsync(parsedStatus, page, pageSize, ct);

        return new PagedResult<AdminValuationInquirySummaryDto>
        {
            Items = result.Items.Select(ToSummary).ToArray(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
    }

    public async Task<IReadOnlyList<AdminOfficeValuationStatisticsDto>> GetOfficeStatisticsAsync(
        CancellationToken ct = default)
    {
        var counts = await _invitations.GetInvitationCountsByAgencyAsync(ct);
        if (counts.Count == 0)
            return [];

        var withinSlaByAgency = await _responses.GetWithinSlaResponseCountsByAgencyAsync(ct);

        var agencyIds = counts.Select(c => c.AgencyId).ToArray();
        var agencies = await _agencies.GetByIdsAsync(agencyIds, ct);
        var agencyById = agencies.ToDictionary(a => a.Id);

        var dtos = new List<AdminOfficeValuationStatisticsDto>(counts.Count);

        foreach (var row in counts)
        {
            // An agency can in principle have been deleted after sending invitations — the
            // statistics row still exists for the history, just without a live name/flag.
            agencyById.TryGetValue(row.AgencyId, out var agency);

            var withinSla = withinSlaByAgency.TryGetValue(row.AgencyId, out var c) ? c : 0;

            dtos.Add(new AdminOfficeValuationStatisticsDto
            {
                AgencyId = row.AgencyId,
                AgencyName = agency?.Name ?? "(محذوف)",
                TotalInvitations = row.TotalInvitations,
                TotalResponses = row.TotalResponses,
                ResponsesWithinSla = withinSla,
                ResponseRate = Rate(row.TotalResponses, row.TotalInvitations),
                SlaComplianceRate = Rate(withinSla, row.TotalInvitations),
                RequiresManualReview = agency?.RequiresManualReview ?? false,
                ManualReviewReason = agency?.ManualReviewReason,
                ManualReviewFlaggedAt = agency?.ManualReviewFlaggedAt
            });
        }

        return dtos
            .OrderByDescending(d => d.TotalInvitations)
            .ToList();
    }

    public async Task<AdminOperationResult> FlagOfficeForReviewAsync(
        Guid agencyId,
        string reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return AdminOperationResult.BadRequest("سبب تعليم المكتب للمراجعة مطلوب.");
        }

        var agency = await _agencies.GetByIdAsync(agencyId, ct);
        if (agency is null)
        {
            return new AdminOperationResult { Succeeded = false, NotFound = true, Message = "Agency not found." };
        }

        var oldSnapshot = SnapshotFlag(agency);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            agency.FlagForManualReview(reason, DateTime.UtcNow);

            _agencies.Update(agency);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.ValuationOfficeFlaggedForReviewByAdmin,
            performedByUserId, ipAddress, agencyId, reason,
            oldSnapshot, SnapshotFlag(agency), ct);

        _logger.LogInformation(
            "Valuation office flagged for manual review. AgencyId={AgencyId}, By={By}",
            agencyId, performedByUserId);

        return AdminOperationResult.Ok("تم تعليم المكتب للمراجعة اليدوية.");
    }

    public async Task<AdminOperationResult> ClearOfficeReviewFlagAsync(
        Guid agencyId,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var agency = await _agencies.GetByIdAsync(agencyId, ct);
        if (agency is null)
        {
            return new AdminOperationResult { Succeeded = false, NotFound = true, Message = "Agency not found." };
        }

        var oldSnapshot = SnapshotFlag(agency);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            agency.ClearManualReviewFlag(DateTime.UtcNow);

            _agencies.Update(agency);
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.ValuationOfficeReviewFlagClearedByAdmin,
            performedByUserId, ipAddress, agencyId, reason: null,
            oldSnapshot, SnapshotFlag(agency), ct);

        _logger.LogInformation(
            "Valuation office manual-review flag cleared. AgencyId={AgencyId}, By={By}",
            agencyId, performedByUserId);

        return AdminOperationResult.Ok("تم إلغاء علم المراجعة عن المكتب.");
    }

    /// <summary>
    /// A ratio in [0, 1], never a division by zero — an office with zero invitations has an
    /// undefined (not zero) rate in reality, but 0 is the safer display default than throwing
    /// or showing NaN on the dashboard.
    /// </summary>
    private static decimal Rate(int numerator, int denominator)
        => denominator <= 0 ? 0m : Math.Round((decimal)numerator / denominator, 4);

    private static AdminValuationInquirySummaryDto ToSummary(ValuationInquiry i) => new()
    {
        Id = i.Id,
        RequesterId = i.RequesterId,
        Status = i.Status.ToString(),
        GovernorateId = i.GovernorateId,
        DistrictId = i.DistrictId,
        NeighborhoodId = i.NeighborhoodId,
        PropertyTypeId = i.PropertyTypeId,
        Area = i.Area,
        Rooms = i.Rooms,
        RequestType = i.RequestType.ToString(),
        CreatedAt = i.CreatedAt,
        ExpiresAt = i.ExpiresAt
    };

    private static object SnapshotFlag(Agency a) => new
    {
        requiresManualReview = a.RequiresManualReview,
        manualReviewReason = a.ManualReviewReason,
        manualReviewFlaggedAt = a.ManualReviewFlaggedAt
    };

    private Task LogAsync(
        string action,
        Guid performedByUserId,
        string? ipAddress,
        Guid agencyId,
        string? reason,
        object oldValue,
        object newValue,
        CancellationToken ct)
        => _auditLogs.LogAsync(
            userId: performedByUserId,
            action: action,
            ipAddress: ipAddress,
            oldValue: JsonSerializer.Serialize(new { agencyId, snapshot = oldValue }),
            newValue: JsonSerializer.Serialize(new
            {
                agencyId,
                snapshot = newValue,
                reason,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);
}
