using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.Queries.GetMyAgencyValuationInquiries;

/// <summary>
/// Stage 6 — the office dashboard. ActorUserId is resolved to the caller's own
/// UserAccount.AgencyId server-side — a caller can never pass an AgencyId directly and read
/// another office's invitations (same discipline GetProviderServiceRequestsQuery already
/// applies to ServiceProvider.UserId).
/// </summary>
public sealed record GetMyAgencyValuationInquiriesQuery(Guid ActorUserId)
    : IRequest<IReadOnlyList<ValuationOfficeInvitationDashboardDto>>;

public sealed class GetMyAgencyValuationInquiriesQueryHandler
    : IRequestHandler<GetMyAgencyValuationInquiriesQuery, IReadOnlyList<ValuationOfficeInvitationDashboardDto>>
{
    private readonly IAgencyRepository _agencies;
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IValuationOfficeResponseRepository _responses;
    private readonly IValuationContactConsentRepository _consents;

    public GetMyAgencyValuationInquiriesQueryHandler(
        IAgencyRepository agencies,
        IValuationOfficeInvitationRepository invitations,
        IValuationInquiryRepository inquiries,
        IValuationOfficeResponseRepository responses,
        IValuationContactConsentRepository consents)
    {
        _agencies = agencies;
        _invitations = invitations;
        _inquiries = inquiries;
        _responses = responses;
        _consents = consents;
    }

    public async Task<IReadOnlyList<ValuationOfficeInvitationDashboardDto>> Handle(
        GetMyAgencyValuationInquiriesQuery request,
        CancellationToken ct)
    {
        var member = await _agencies.GetUserAccountAsync(request.ActorUserId, ct)
            ?? throw new NotFoundException("الحساب غير موجود.");

        if (member.AgencyId is not { } agencyId)
            throw new NotFoundException("لا يوجد مكتب عقاري مرتبط بهذا الحساب.");

        var invitations = await _invitations.GetByAgencyIdAsync(agencyId, ct);
        if (invitations.Count == 0)
            return [];

        var now = DateTime.UtcNow;

        // One round trip for every consent this agency has ever received, rather than one
        // GetByInvitationIdAsync call per row below — same batching reasoning this handler
        // already accepts for the per-row inquiry/response N+1 (see the loop's own comment),
        // just done once up front since this dictionary is cheap to build.
        var consentByInvitationId = (await _consents.GetByAgencyIdAsync(agencyId, ct))
            .ToDictionary(c => c.InvitationId);

        // Per-row enrichment (parent inquiry + own response, if any) — same accepted N+1-per-
        // row style GetMyAgencyInvitationsQueryHandler already uses for its own "my inbox"
        // list; not something to refactor away as part of Stage 6.
        var dtos = new List<ValuationOfficeInvitationDashboardDto>(invitations.Count);

        foreach (var invitation in invitations)
        {
            var inquiry = await _inquiries.GetByIdAsync(invitation.InquiryId, ct);
            if (inquiry is null)
                continue;

            var response = await _responses.GetByInvitationIdAsync(invitation.Id, ct);

            var canRespond =
                invitation.Status == ValuationOfficeInvitationStatus.Sent
                && !invitation.IsExpired(now, inquiry.ExpiresAt);

            consentByInvitationId.TryGetValue(invitation.Id, out var consent);

            dtos.Add(new ValuationOfficeInvitationDashboardDto(
                InvitationId: invitation.Id,
                InquiryId: invitation.InquiryId,
                MatchLevel: invitation.MatchLevel,
                InvitationStatus: invitation.Status,
                SentAt: invitation.SentAt,
                RespondedAt: invitation.RespondedAt,
                CanRespond: canRespond,
                GovernorateId: inquiry.GovernorateId,
                DistrictId: inquiry.DistrictId,
                NeighborhoodId: inquiry.NeighborhoodId,
                PropertyTypeId: inquiry.PropertyTypeId,
                Area: inquiry.Area,
                Rooms: inquiry.Rooms,
                RequestType: inquiry.RequestType,
                InquiryStatus: inquiry.Status,
                InquiryExpiresAt: inquiry.ExpiresAt,
                MyEstimatedPrice: response?.EstimatedPrice,
                MyNotes: response?.Notes,
                CustomerContactPhone: consent?.ContactPhone,
                CustomerContactEmail: consent?.ContactEmail));
        }

        return dtos
            .OrderByDescending(d => d.SentAt)
            .ToList();
    }
}
