using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Domain.Valuation.Entities;

/// <summary>
/// One agency's invitation to submit a <see cref="ValuationOfficeResponse"/> for a given
/// <see cref="ValuationInquiry"/>. DDD: private setters + factory method + domain
/// state-machine methods, same shape as AgencyInvitation (Sent is the only state anything
/// transitions OUT of — Responded and Expired are both terminal).
///
/// No FK/EF relationship to Agency or ValuationInquiry is declared here — <see
/// cref="AgencyId"/>/<see cref="InquiryId"/> are plain Guid references, same as this phase's
/// other entities; wiring the actual relationships is Infrastructure's job in a later phase.
/// </summary>
public sealed class ValuationOfficeInvitation : BaseEntity
{
    private ValuationOfficeInvitation() { }

    public Guid AgencyId { get; private set; }

    public Guid InquiryId { get; private set; }

    public ValuationOfficeInvitationStatus Status { get; private set; } = ValuationOfficeInvitationStatus.Sent;

    /// <summary>
    /// How precisely this agency was matched to the inquiry's location. Set once at creation
    /// and never recomputed here — the matching algorithm that decides it belongs to the
    /// Application layer (see ValuationMatchLevel's doc comment).
    /// </summary>
    public ValuationMatchLevel MatchLevel { get; private set; }

    public DateTime SentAt { get; private set; }

    /// <summary>
    /// Null until the office responds. Kept private-set and only ever written together with
    /// the transition to <see cref="ValuationOfficeInvitationStatus.Responded"/> in
    /// <see cref="MarkResponded"/> — so "Status == Sent" and "RespondedAt != null" can never
    /// both be true; there is no separate setter that could let them drift apart.
    /// </summary>
    public DateTime? RespondedAt { get; private set; }

    /// <summary>
    /// Remediation M3 — stamped only once <see cref="Notifications.Interfaces.INotificationService.NotifyValuationOfficeInvitationExpiredAsync"/>
    /// actually succeeds for this invitation. Null means "Expired, but the notification has
    /// not yet been confirmed delivered" — the signal ValuationSlaEnforcementService's own
    /// retry pass uses, same "idempotency stamp, set on success only" pattern as
    /// ValuationInquiry.ExpiryNotifiedAt/Property.ExpiryWarningSentAt.
    /// </summary>
    public DateTime? ExpiryNotifiedAt { get; private set; }

    public static ValuationOfficeInvitation Create(
        Guid agencyId,
        Guid inquiryId,
        ValuationMatchLevel matchLevel,
        DateTime utcNow)
    {
        if (agencyId == Guid.Empty)
            throw new DomainException("معرّف المكتب العقاري مطلوب.");

        if (inquiryId == Guid.Empty)
            throw new DomainException("معرّف طلب التقييم مطلوب.");

        if (!Enum.IsDefined(matchLevel))
            throw new DomainException("مستوى المطابقة غير معروف.");

        return new ValuationOfficeInvitation
        {
            AgencyId = agencyId,
            InquiryId = inquiryId,
            MatchLevel = matchLevel,
            Status = ValuationOfficeInvitationStatus.Sent,
            SentAt = utcNow,
            RespondedAt = null,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    /// <summary>
    /// Called once the office's <see cref="ValuationOfficeResponse"/> is recorded (a later
    /// phase's handler's job — this method just moves the invitation itself into Responded).
    /// </summary>
    public void MarkResponded(DateTime utcNow)
    {
        EnsureStatus(ValuationOfficeInvitationStatus.Sent, "تسجيل رد");

        Status = ValuationOfficeInvitationStatus.Responded;
        RespondedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// True once the invitation is still Sent but past due. The actual expiry window/trigger
    /// (e.g. tied to the parent ValuationInquiry.ExpiresAt, or its own independent window) is
    /// an open decision for the Application layer — see this module's final report — so this
    /// takes the cutoff as a parameter rather than assuming one.
    /// </summary>
    public bool IsExpired(DateTime utcNow, DateTime expiresAt)
        => Status == ValuationOfficeInvitationStatus.Sent && utcNow >= expiresAt;

    public void Expire(DateTime utcNow)
    {
        EnsureStatus(ValuationOfficeInvitationStatus.Sent, "انتهاء صلاحية");

        Status = ValuationOfficeInvitationStatus.Expired;
        UpdatedAt = utcNow;
    }

    private void EnsureStatus(ValuationOfficeInvitationStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException($"لا يمكن {action} لدعوة مكتب في الحالة '{Status}'.");
    }

    /// <summary>Remediation M3 — marks the ValuationOfficeInvitationExpired notification as
    /// successfully delivered. No status guard, same as Property.MarkExpiryWarningSent — a
    /// plain idempotency stamp, not a state transition.</summary>
    public void MarkExpiryNotified(DateTime utcNow)
    {
        ExpiryNotifiedAt = utcNow;
        UpdatedAt = utcNow;
    }
}
