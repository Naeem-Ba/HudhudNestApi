using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Queries.GetValuationInquiryStatus;

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

    public GetValuationInquiryStatusQueryHandler(
        IValuationInquiryRepository inquiries,
        IValuationOfficeInvitationRepository invitations,
        IValuationOfficeResponseRepository responses)
    {
        _inquiries = inquiries;
        _invitations = invitations;
        _responses = responses;
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

        var estimates = new List<ValuationEstimateDto>();

        foreach (var invitation in invitations)
        {
            if (invitation.Status != ValuationOfficeInvitationStatus.Responded)
                continue;

            var response = await _responses.GetByInvitationIdAsync(invitation.Id, ct);
            if (response is null)
                continue;

            estimates.Add(new ValuationEstimateDto(
                response.EstimatedPrice, response.Notes, response.SubmittedAt, invitation.MatchLevel));
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
