using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.Queries.GetValuationInquiryStatus;

/// <summary>
/// Stage 7 — the customer's manual "check my request" refresh (this codebase's established
/// convention for async status — see my-service-requests/visits pages on the Angular side —
/// is a manual-refresh list/status view, not polling).
///
/// ActorUserId is nullable and resolved by the Controller from the auth token, exactly like
/// CreateValuationInquiryCommand.RequesterId.
/// </summary>
public sealed record GetValuationInquiryStatusQuery(Guid InquiryId, Guid? ActorUserId)
    : IRequest<ValuationInquiryStatusDto>;

public sealed class GetValuationInquiryStatusQueryHandler
    : IRequestHandler<GetValuationInquiryStatusQuery, ValuationInquiryStatusDto>
{
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IValuationOfficeResponseRepository _responses;
    private readonly IValuationContactConsentRepository _consents;
    private readonly IAgencyRepository _agencies;

    public GetValuationInquiryStatusQueryHandler(
        IValuationInquiryRepository inquiries,
        IValuationOfficeInvitationRepository invitations,
        IValuationOfficeResponseRepository responses,
        IValuationContactConsentRepository consents,
        IAgencyRepository agencies)
    {
        _inquiries = inquiries;
        _invitations = invitations;
        _responses = responses;
        _consents = consents;
        _agencies = agencies;
    }

    public async Task<ValuationInquiryStatusDto> Handle(
        GetValuationInquiryStatusQuery request,
        CancellationToken ct)
    {
        var inquiry = await _inquiries.GetByIdAsync(request.InquiryId, ct)
            ?? throw new NotFoundException("طلب التقييم غير موجود.");

        // Authorization: an inquiry tied to a real account may only be viewed by that same
        // account. An anonymous (guest) inquiry has RequesterId == null — there is no account
        // to check against, so its own hard-to-guess InquiryId is the only access control that
        // exists for it, the same reliance this module's Fast Path already places on the id
        // alone for correlation. This means request.ActorUserId being null (an unauthenticated
        // caller) is only ever rejected when the inquiry DOES belong to someone.
        if (inquiry.RequesterId.HasValue && request.ActorUserId != inquiry.RequesterId)
            throw new ForbiddenException("لا يمكنك عرض طلب تقييم لا يخصك.");

        var invitations = await _invitations.GetByInquiryIdAsync(inquiry.Id, ct);

        var responded = invitations
            .Where(i => i.Status == ValuationOfficeInvitationStatus.Responded)
            .ToList();

        if (responded.Count == 0)
        {
            return new ValuationInquiryStatusDto
            {
                InquiryId = inquiry.Id,
                Status = inquiry.Status,
                ExpiresAt = inquiry.ExpiresAt,
                Estimates = [],
            };
        }

        // Batch-resolve agency names in one round trip rather than one GetByIdAsync call per
        // responded invitation — same reasoning AdminValuationInquiryService's own office
        // statistics use IAgencyRepository.GetByIdsAsync for.
        var agencyIds = responded.Select(i => i.AgencyId).Distinct().ToArray();
        var agencyById = (await _agencies.GetByIdsAsync(agencyIds, ct)).ToDictionary(a => a.Id);

        var estimates = new List<ValuationEstimateDto>();

        foreach (var invitation in responded)
        {
            var response = await _responses.GetByInvitationIdAsync(invitation.Id, ct);
            if (response is null)
                continue;

            var consent = await _consents.GetByInvitationIdAsync(invitation.Id, ct);
            agencyById.TryGetValue(invitation.AgencyId, out var agency);

            estimates.Add(new ValuationEstimateDto(
                InvitationId: invitation.Id,
                AgencyId: invitation.AgencyId,
                AgencyName: agency?.Name ?? "(محذوف)",
                EstimatedPrice: response.EstimatedPrice,
                Notes: response.Notes,
                SubmittedAt: response.SubmittedAt,
                MatchLevel: invitation.MatchLevel,
                ContactConsentGiven: consent is not null));
        }

        return new ValuationInquiryStatusDto
        {
            InquiryId = inquiry.Id,
            Status = inquiry.Status,
            ExpiresAt = inquiry.ExpiresAt,
            Estimates = estimates.OrderBy(e => e.SubmittedAt).ToList(),
        };
    }
}
