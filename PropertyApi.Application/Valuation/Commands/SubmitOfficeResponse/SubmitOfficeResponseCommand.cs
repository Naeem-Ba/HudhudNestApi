using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Valuation.Entities;

namespace PropertyApi.Application.Valuation.Commands.SubmitOfficeResponse;

/// <summary>
/// Stage 6 — an invited office submits its price estimate. ActorUserId is compared against
/// the caller's own UserAccount.AgencyId, resolved server-side — never against a
/// client-supplied agency id (same discipline AcceptServiceRequestCommand/
/// RejectServiceRequestCommand already apply to ServiceProvider.UserId).
/// </summary>
public sealed record SubmitOfficeResponseCommand(
    Guid InvitationId,
    Guid ActorUserId,
    decimal EstimatedPrice,
    string? Notes) : IRequest<ValuationOfficeResponseDto>;

public sealed class SubmitOfficeResponseCommandValidator : AbstractValidator<SubmitOfficeResponseCommand>
{
    public SubmitOfficeResponseCommandValidator()
    {
        RuleFor(x => x.EstimatedPrice)
            .GreaterThan(0).WithMessage("السعر المقدَّر يجب أن يكون أكبر من صفر.");

        // ValuationOfficeResponse.Notes itself has no length cap (see its own doc comment) —
        // this is this layer's own bound, same convention AddServiceReviewCommandValidator
        // applies to ServiceReview.Comment.
        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .When(x => x.Notes is not null);
    }
}

public sealed class SubmitOfficeResponseCommandHandler
    : IRequestHandler<SubmitOfficeResponseCommand, ValuationOfficeResponseDto>
{
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IValuationOfficeResponseRepository _responses;
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitOfficeResponseCommandHandler> _logger;

    public SubmitOfficeResponseCommandHandler(
        IValuationOfficeInvitationRepository invitations,
        IValuationOfficeResponseRepository responses,
        IValuationInquiryRepository inquiries,
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork,
        ILogger<SubmitOfficeResponseCommandHandler> logger)
    {
        _invitations = invitations;
        _responses = responses;
        _inquiries = inquiries;
        _agencies = agencies;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ValuationOfficeResponseDto> Handle(
        SubmitOfficeResponseCommand request,
        CancellationToken ct)
    {
        var invitation = await _invitations.GetByIdAsync(request.InvitationId, ct)
            ?? throw new NotFoundException("دعوة التقييم غير موجودة.");

        var member = await _agencies.GetUserAccountAsync(request.ActorUserId, ct)
            ?? throw new NotFoundException("الحساب غير موجود.");

        // Cross-agency authorization: an invitation targets the office as an organization,
        // not one specific person (unlike AgencyInvitation.TargetUserId) — any member of the
        // invited agency may respond on its behalf. member.AgencyId is resolved server-side
        // from the caller's own account; nothing here ever trusts a client-supplied agency id.
        if (member.AgencyId != invitation.AgencyId)
            throw new ForbiddenException("لا يمكنك الرد على دعوة تقييم موجّهة لمكتب آخر.");

        var inquiry = await _inquiries.GetByIdAsync(invitation.InquiryId, ct)
            ?? throw new NotFoundException("طلب التقييم غير موجود.");

        var now = DateTime.UtcNow;

        // Backend-enforced expiry, independent of ValuationInquiryExpiryHostedService — that
        // sweep only ticks every 15 minutes, so Status can briefly still read Sent after the
        // real cutoff has passed. A disabled frontend button is not enforcement; this re-derives
        // the same IsExpired(utcNow, expiresAt) check the sweep itself uses and, finding it
        // true, expires the invitation right now rather than letting a late response through.
        if (invitation.IsExpired(now, inquiry.ExpiresAt))
        {
            invitation.Expire(now);
            _invitations.Update(invitation);
            await _unitOfWork.SaveChangesAsync(ct);

            throw new ConflictException("انتهت مهلة الرد على هذه الدعوة.");
        }

        try
        {
            // Guards Sent -> Responded only — an invitation already Responded (submitting
            // twice) or already Expired throws DomainException here, translated to the same
            // "wrong state" ConflictException AcceptAgencyInvitationCommandHandler uses for
            // its own domain guard.
            invitation.MarkResponded(now);
        }
        catch (DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        var response = ValuationOfficeResponse.Create(
            invitation.Id, request.EstimatedPrice, now, request.Notes);

        _invitations.Update(invitation);
        await _responses.AddAsync(response, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Valuation office response submitted. InvitationId={InvitationId}, AgencyId={AgencyId}, InquiryId={InquiryId}",
            invitation.Id,
            invitation.AgencyId,
            invitation.InquiryId);

        return new ValuationOfficeResponseDto(
            response.Id, invitation.Id, response.EstimatedPrice, response.Notes, response.SubmittedAt);
    }
}
