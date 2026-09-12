using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Commands.SubmitValuationContactConsent;

/// <summary>
/// Stage 9 — "after seeing this office's estimate, I choose to let them contact me": the one
/// and only place in this module where customer contact information is allowed to move toward
/// an office, and only for the one invitation the customer explicitly names.
///
/// InvitationId alone (not AgencyId) is what the caller supplies — the handler resolves which
/// agency that invitation belongs to itself, exactly like SubmitOfficeResponseCommand resolves
/// its own agency from the invitation rather than trusting a client-supplied id (this
/// codebase's standing "never trust a client-supplied ownership id" rule). ActorUserId is
/// nullable and resolved by the Controller from the auth token, same as every other
/// Valuation customer-facing command.
/// </summary>
public sealed record SubmitValuationContactConsentCommand(
    Guid InquiryId,
    Guid InvitationId,
    Guid? ActorUserId,
    string? ContactPhone,
    string? ContactEmail) : IRequest<ValuationContactConsentResultDto>;

public sealed class SubmitValuationContactConsentCommandValidator
    : AbstractValidator<SubmitValuationContactConsentCommand>
{
    public SubmitValuationContactConsentCommandValidator()
    {
        RuleFor(x => x.InquiryId).NotEmpty();
        RuleFor(x => x.InvitationId).NotEmpty();

        // Field-presence check here; ValuationContactConsent.Create enforces the same
        // invariant again as a domain-level defense (same duplication style
        // CreateValuationInquiryCommandValidator already accepts for GovernorateId).
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ContactPhone) || !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("يجب تقديم رقم هاتف أو بريد إلكتروني واحد على الأقل للمشاركة.");
    }
}

public sealed class SubmitValuationContactConsentCommandHandler
    : IRequestHandler<SubmitValuationContactConsentCommand, ValuationContactConsentResultDto>
{
    private readonly IValuationInquiryRepository _inquiries;
    private readonly IValuationOfficeInvitationRepository _invitations;
    private readonly IValuationContactConsentRepository _consents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitValuationContactConsentCommandHandler> _logger;

    public SubmitValuationContactConsentCommandHandler(
        IValuationInquiryRepository inquiries,
        IValuationOfficeInvitationRepository invitations,
        IValuationContactConsentRepository consents,
        IUnitOfWork unitOfWork,
        ILogger<SubmitValuationContactConsentCommandHandler> logger)
    {
        _inquiries = inquiries;
        _invitations = invitations;
        _consents = consents;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ValuationContactConsentResultDto> Handle(
        SubmitValuationContactConsentCommand request,
        CancellationToken ct)
    {
        var inquiry = await _inquiries.GetByIdAsync(request.InquiryId, ct)
            ?? throw new NotFoundException("طلب التقييم غير موجود.");

        // Same ownership rule GetValuationInquiryStatusQueryHandler already enforces for
        // itself — see its own doc comment.
        if (inquiry.RequesterId.HasValue && request.ActorUserId != inquiry.RequesterId)
            throw new ForbiddenException("لا يمكنك التصرف بطلب تقييم لا يخصك.");

        var invitation = await _invitations.GetByIdAsync(request.InvitationId, ct);

        // Cross-inquiry IDOR guard: an invitation that exists but belongs to a DIFFERENT
        // inquiry must be rejected exactly like a missing one — the caller already proved
        // (via the ownership check above) they may act on request.InquiryId, but that grants
        // them nothing over an invitation that happens to belong to someone else's inquiry.
        if (invitation is null || invitation.InquiryId != inquiry.Id)
            throw new NotFoundException("دعوة المكتب غير موجودة ضمن هذا الطلب.");

        // Idempotent: re-submitting consent for the same invitation returns the existing
        // record instead of creating (or attempting to create, and failing the unique index)
        // a duplicate — this module's own idempotency rule, applied to a customer-triggered
        // write for the same reason it is applied to the background-triggered ones.
        var existing = await _consents.GetByInvitationIdAsync(invitation.Id, ct);
        if (existing is not null)
        {
            return new ValuationContactConsentResultDto(existing.InvitationId, existing.ConsentedAt);
        }

        // "After the customer sees the responses" — consenting to share contact info makes no
        // sense before the office has actually responded with anything to react to.
        if (invitation.Status != ValuationOfficeInvitationStatus.Responded)
        {
            throw new ConflictException("لا يمكن الموافقة على مشاركة بيانات التواصل قبل استلام رد من هذا المكتب.");
        }

        var now = DateTime.UtcNow;

        var consent = ValuationContactConsent.Create(
            inquiry.Id, invitation.Id, invitation.AgencyId,
            request.ContactPhone, request.ContactEmail, now);

        await _consents.AddAsync(consent, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Valuation contact consent granted. InquiryId={InquiryId}, InvitationId={InvitationId}, AgencyId={AgencyId}",
            inquiry.Id, invitation.Id, invitation.AgencyId);

        return new ValuationContactConsentResultDto(consent.InvitationId, consent.ConsentedAt);
    }
}
