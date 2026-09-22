using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Domain.Bookings.Entities;

/// <summary>
/// Represents a visitor's request to physically visit a property.
/// DDD: private setters + factory method + domain state-machine methods.
/// Status transitions: Pending → Confirmed | Declined | Cancelled | RescheduleProposed
///                     RescheduleProposed → Confirmed | Declined | Cancelled
///                     Confirmed → Completed | Cancelled
/// </summary>
public sealed class VisitRequest : BaseEntity
{
    // ── References ────────────────────────────────────────────────
    public Guid PropertyId { get; private set; }
    public Guid RequesterId { get; private set; }

    // ── Visitor info (denormalized for history) ───────────────────
    public string VisitorName { get; private set; } = string.Empty;
    public string VisitorPhone { get; private set; } = string.Empty;
    public string? VisitorNote { get; private set; }

    // ── Scheduling ────────────────────────────────────────────────
    /// <summary>Proposed visit date-time in UTC.</summary>
    public DateTime ProposedAt { get; private set; }

    // ── Owner response ────────────────────────────────────────────
    public string? OwnerNote { get; private set; }
    public DateTime? RespondedAt { get; private set; }

    // ── State ─────────────────────────────────────────────────────
    public VisitStatus Status { get; private set; } = VisitStatus.Pending;

    // ── Navigation (EF) ───────────────────────────────────────────
    public Property? Property { get; private set; }
    public UserAccount? Requester { get; private set; }

    // ── EF Core private constructor ───────────────────────────────
    private VisitRequest() { }

    // ── Factory ───────────────────────────────────────────────────
    public static VisitRequest Create(
        Guid propertyId,
        Guid requesterId,
        DateTime proposedAt,
        string visitorName,
        string visitorPhone,
        string? visitorNote = null)
    {
        if (proposedAt < DateTime.UtcNow.AddHours(2))
            throw new DomainException("يجب أن يكون الحجز قبل موعد الزيارة بساعتين على الأقل.");

        if (proposedAt > DateTime.UtcNow.AddDays(90))
            throw new DomainException("لا يمكن الحجز لأكثر من 90 يومًا مقدمًا.");

        return new VisitRequest
        {
            PropertyId = propertyId,
            RequesterId = requesterId,
            ProposedAt = proposedAt,
            VisitorName = visitorName.Trim(),
            VisitorPhone = visitorPhone.Trim(),
            VisitorNote = visitorNote?.Trim(),
        };
    }

    // ── Domain State-Machine ──────────────────────────────────────
    public void Confirm(string? ownerNote = null)
    {
        EnsureStatus(VisitStatus.Pending, "تأكيد");
        Status = VisitStatus.Confirmed;
        OwnerNote = ownerNote?.Trim();
        RespondedAt = DateTime.UtcNow;
    }

    public void Decline(string? reason = null)
    {
        EnsureStatus(VisitStatus.Pending, "رفض");
        Status = VisitStatus.Declined;
        OwnerNote = reason?.Trim();
        RespondedAt = DateTime.UtcNow;
    }

    public void Cancel(Guid actorId)
    {
        if (Status is VisitStatus.Completed or VisitStatus.Declined)
            throw new DomainException($"لا يمكن إلغاء الزيارة بعد أن أصبحت {Status}.");

        if (RequesterId != actorId)
            throw new DomainException("المشغّل الحالي غير مخوّل لإلغاء هذه الزيارة.");

        Status = VisitStatus.Cancelled;
    }

    public void Complete()
    {
        EnsureStatus(VisitStatus.Confirmed, "إتمام");
        Status = VisitStatus.Completed;
    }

    /// <summary>
    /// Owner-side: instead of confirming/declining the requester's proposed time as-is,
    /// counter-propose a different date/time. Awaits the requester's AcceptReschedule/
    /// DeclineReschedule. Only valid from Pending — mirrors Confirm/Decline's precondition.
    /// </summary>
    public void ProposeAlternate(DateTime newProposedAt, string? ownerNote = null)
    {
        EnsureStatus(VisitStatus.Pending, "اقتراح موعد بديل");

        if (newProposedAt < DateTime.UtcNow.AddHours(2))
            throw new DomainException("يجب أن يكون الموعد البديل بعد ساعتين على الأقل من الآن.");

        if (newProposedAt > DateTime.UtcNow.AddDays(90))
            throw new DomainException("لا يمكن اقتراح موعد بديل لأكثر من 90 يومًا مقدمًا.");

        Status = VisitStatus.RescheduleProposed;
        ProposedAt = newProposedAt;
        OwnerNote = ownerNote?.Trim();
        RespondedAt = DateTime.UtcNow;
    }

    /// <summary>Requester-side: accepts the owner's counter-proposed date/time.</summary>
    public void AcceptReschedule()
    {
        EnsureStatus(VisitStatus.RescheduleProposed, "قبول الموعد البديل");
        Status = VisitStatus.Confirmed;
        RespondedAt = DateTime.UtcNow;
    }

    /// <summary>Requester-side: declines the owner's counter-proposed date/time.</summary>
    public void DeclineReschedule()
    {
        EnsureStatus(VisitStatus.RescheduleProposed, "رفض الموعد البديل");
        Status = VisitStatus.Declined;
        RespondedAt = DateTime.UtcNow;
    }

    // ── Private Helpers ───────────────────────────────────────────
    private void EnsureStatus(VisitStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException(
                $"لا يمكن {action} زيارة في الحالة '{Status}'.");
    }
}