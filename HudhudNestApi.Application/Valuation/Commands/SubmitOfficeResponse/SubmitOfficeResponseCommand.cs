using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.Commands.SubmitOfficeResponse;

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
    private readonly INotificationService _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubmitOfficeResponseCommandHandler> _logger;

    public SubmitOfficeResponseCommandHandler(
        IValuationOfficeInvitationRepository invitations,
        IValuationOfficeResponseRepository responses,
        IValuationInquiryRepository inquiries,
        IAgencyRepository agencies,
        INotificationService notifications,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<SubmitOfficeResponseCommandHandler> logger)
    {
        _invitations = invitations;
        _responses = responses;
        _inquiries = inquiries;
        _agencies = agencies;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _clock = clock;
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

        // Remediation L1 — TimeProvider, not DateTime.UtcNow directly, for the same reason
        // ValuationInquiryExpiryHostedService already uses it: lets a test control "now" via
        // dependency injection instead of only via pre-constructed domain state, and keeps
        // this request-path handler on the same time-abstraction convention as the sweep it
        // races against.
        var now = _clock.GetUtcNow().UtcDateTime;

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

        // Step 1 (required): the invitation's Sent->Responded transition and the new response
        // row. ValuationOfficeInvitation now carries an xmin concurrency token (Remediation
        // H1): if ValuationInquiryExpiryHostedService's sweep concurrently expired THIS
        // invitation between this request's read and this save, Postgres reports 0 rows
        // matched and EF Core throws DbUpdateConcurrencyException — ExceptionHandlingMiddleware
        // already maps that to 409 (RELEASE-BLOCKERS-AR.md B-9's existing, unmodified
        // behavior), so a genuine "expiry won the race" is a clean, deterministic rejection
        // with zero rows written — never a persisted-but-invisible response. This call must be
        // allowed to throw and fail the whole request: it is the one thing here that is not
        // optional.
        await _unitOfWork.SaveChangesAsync(ct);

        // Step 2 (best-effort, Remediation H2): only after the response above is durably
        // committed, separately check whether this was the last outstanding invitation for the
        // inquiry — "nothing left outstanding", not an invented "3 responses" constant, so it
        // completes correctly whether OfficeMatchingService sent 1, 2, 3, or more invitations.
        // Deliberately its own SaveChangesAsync, not batched with Step 1: two offices
        // responding to two DIFFERENT invitations of the SAME inquiry at nearly the same
        // instant would otherwise let whichever loses the shared ValuationInquiry row's xmin
        // race take the winner's own already-valid response down with it. TrySaveChangesAsync
        // (Remediation H1) turns "someone else already resolved this inquiry" (a concurrent
        // sibling response, or the SLA sweep) into a plain `false` instead of an exception —
        // this office's response from Step 1 is unaffected either way.
        var willComplete = false;
        var siblings = await _invitations.GetByInquiryIdAsync(invitation.InquiryId, ct);
        if (siblings.All(i => i.Status != ValuationOfficeInvitationStatus.Sent))
        {
            try
            {
                // MarkCompleted's own Domain guard (MatchedFromListings or
                // AwaitingOfficeResponses only) is the sole source of truth here — not
                // duplicated or second-guessed by this handler.
                inquiry.MarkCompleted(now);
                _inquiries.Update(inquiry);
                willComplete = await _unitOfWork.TrySaveChangesAsync(ct);
            }
            catch (DomainException)
            {
                // inquiry was already Completed/Expired by the time this handler read it —
                // this office's response (Step 1) is already safely persisted regardless.
            }
        }

        _logger.LogInformation(
            "Valuation office response submitted. InvitationId={InvitationId}, AgencyId={AgencyId}, InquiryId={InquiryId}, InquiryCompleted={InquiryCompleted}",
            invitation.Id,
            invitation.AgencyId,
            invitation.InquiryId,
            willComplete);

        if (willComplete && inquiry.RequesterId is { } requesterId)
        {
            // Same "guest inquiry has no account to notify" rule ValuationSlaEnforcementService
            // already applies to the Expired notification — never raised for an anonymous
            // inquiry. Sent only after the domain transition is durably committed above, same
            // "mutate+save first, notify after" ordering this module already uses throughout.
            try
            {
                await _notifications.NotifyValuationResultReadyAsync(requesterId, inquiry.Id, ct);

                // Remediation M3 — stamped only on confirmed success, mirroring
                // ValuationSlaEnforcementService's own ExpiryNotifiedAt pattern. Best-effort:
                // if THIS save itself loses a concurrency race or fails, the inquiry is picked
                // up by ValuationSlaEnforcementService's retry phase next sweep tick and simply
                // re-notified — a redundant notification here is a rare, acceptable edge case,
                // never a silent loss.
                inquiry.MarkResultReadyNotified(now);
                _inquiries.Update(inquiry);
                await _unitOfWork.TrySaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // A failed notification here must not fail (or appear to fail) a response that
                // was already successfully recorded above. The durable-retry mechanism for
                // this notification type lives in ValuationSlaEnforcementService
                // (GetCompletedAwaitingResultNotificationAsync), which will pick this inquiry
                // up on its next sweep tick since ResultReadyNotifiedAt was never stamped.
                _logger.LogError(
                    ex,
                    "Failed to send valuation-result-ready notification synchronously; it will be retried by the SLA sweep. InquiryId={InquiryId}",
                    inquiry.Id);
            }
        }

        return new ValuationOfficeResponseDto(
            response.Id, invitation.Id, response.EstimatedPrice, response.Notes, response.SubmittedAt);
    }
}
