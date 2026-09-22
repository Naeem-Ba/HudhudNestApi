using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Domain.ShortStay.ValueObjects;

namespace HudhudNestApi.Domain.ShortStay.Entities;

/// <summary>
/// DDD aggregate root for a short-stay reservation. Private setters + factory + guarded
/// state-machine methods, same shape as VisitRequest. See BookingStatus for the full
/// transition diagram.
///
/// CancellationPolicy/HouseRules fields are a point-in-time SNAPSHOT copied from the
/// listing at booking-creation time (see Create) — a later edit to the listing's policy
/// must never retroactively change the terms of an existing booking.
/// </summary>
public sealed class Booking : BaseEntity
{
    // ── References ────────────────────────────────────────────────
    public Guid UnitId { get; private set; }
    public Guid GuestId { get; private set; }

    // ── Stay ──────────────────────────────────────────────────────
    public DateOnly CheckIn { get; private set; }
    public DateOnly CheckOut { get; private set; }
    public GuestComposition Guests { get; private set; } = null!;

    // ── Mode / payment ────────────────────────────────────────────
    public BookingMode Mode { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal DepositAmount { get; private set; }
    public decimal RemainingAmount { get; private set; }

    // ── Policy snapshots (immutable once set at Create) ────────────
    public int CancellationPolicyFreeCancellationDays { get; private set; }
    public bool CancellationPolicyDepositRefundable { get; private set; }
    public string? CancellationPolicyCustomTermsText { get; private set; }
    public string HouseRulesSnapshotText { get; private set; } = string.Empty;
    public DateTime HouseRulesAcceptedAt { get; private set; }

    // ── State ─────────────────────────────────────────────────────
    public BookingStatus Status { get; private set; }
    public string? HostNote { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    public AccommodationUnit? Unit { get; private set; }

    private Booking() { }

    public static Booking Create(
        Guid unitId,
        Guid guestId,
        DateOnly checkIn,
        DateOnly checkOut,
        GuestComposition guests,
        BookingMode mode,
        PaymentMethod paymentMethod,
        decimal totalAmount,
        decimal depositAmount,
        int cancellationPolicyFreeCancellationDays,
        bool cancellationPolicyDepositRefundable,
        string? cancellationPolicyCustomTermsText,
        string houseRulesSnapshotText,
        bool houseRulesAccepted)
    {
        if (checkOut <= checkIn)
            throw new DomainException("تاريخ المغادرة يجب أن يكون بعد تاريخ الوصول.");

        if (checkIn < DateOnly.FromDateTime(DateTime.UtcNow.Date))
            throw new DomainException("لا يمكن الحجز بتاريخ ماضٍ.");

        if (!houseRulesAccepted)
            throw new DomainException("يجب الموافقة على قواعد المنزل قبل إتمام الحجز.");

        if (depositAmount < 0 || depositAmount > totalAmount)
            throw new DomainException("قيمة العربون غير صالحة.");

        var booking = new Booking
        {
            UnitId = unitId,
            GuestId = guestId,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Guests = guests,
            Mode = mode,
            PaymentMethod = paymentMethod,
            TotalAmount = totalAmount,
            DepositAmount = depositAmount,
            RemainingAmount = totalAmount - depositAmount,
            CancellationPolicyFreeCancellationDays = cancellationPolicyFreeCancellationDays,
            CancellationPolicyDepositRefundable = cancellationPolicyDepositRefundable,
            CancellationPolicyCustomTermsText = cancellationPolicyCustomTermsText,
            HouseRulesSnapshotText = houseRulesSnapshotText,
            HouseRulesAcceptedAt = DateTime.UtcNow,
            Status = BookingStatus.Pending,
        };

        // Instant Booking skips host review entirely: no deposit required -> Confirmed
        // directly; deposit required -> waits at Approved for the deposit to be recorded.
        if (mode == BookingMode.Instant)
        {
            booking.Status = depositAmount > 0 ? BookingStatus.Approved : BookingStatus.Confirmed;
        }

        return booking;
    }

    // ── Domain State-Machine ──────────────────────────────────────
    public void Approve(string? hostNote = null)
    {
        EnsureStatus(BookingStatus.Pending, "الموافقة على");
        Status = BookingStatus.Approved;
        HostNote = hostNote?.Trim();
        RespondedAt = DateTime.UtcNow;
    }

    public void Reject(string? reason = null)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Approved))
            throw new InvalidStateTransitionException($"لا يمكن رفض حجز في الحالة '{Status}'.");

        Status = BookingStatus.Rejected;
        HostNote = reason?.Trim();
        RespondedAt = DateTime.UtcNow;
    }

    public void RecordDepositPaid()
    {
        EnsureStatus(BookingStatus.Approved, "تسجيل عربون");
        Status = BookingStatus.DepositPaid;
    }

    public void Confirm()
    {
        if (Status is not (BookingStatus.Approved or BookingStatus.DepositPaid))
            throw new InvalidStateTransitionException($"لا يمكن تأكيد حجز في الحالة '{Status}'.");

        Status = BookingStatus.Confirmed;
    }

    public void CheckInGuest()
    {
        EnsureStatus(BookingStatus.Confirmed, "تسجيل وصول");
        Status = BookingStatus.CheckedIn;
    }

    public void CheckOutGuest()
    {
        EnsureStatus(BookingStatus.CheckedIn, "تسجيل مغادرة");
        Status = BookingStatus.CheckedOut;
    }

    public void Complete()
    {
        EnsureStatus(BookingStatus.CheckedOut, "إتمام");
        Status = BookingStatus.Completed;
    }

    public void Cancel(Guid actorId, string? reason = null)
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled or BookingStatus.Rejected
            or BookingStatus.Expired or BookingStatus.NoShow or BookingStatus.CheckedOut)
            throw new InvalidStateTransitionException($"لا يمكن إلغاء حجز في الحالة '{Status}'.");

        if (GuestId != actorId)
            throw new DomainException("المشغّل الحالي غير مخوّل لإلغاء هذا الحجز.");

        Status = BookingStatus.Cancelled;
        CancellationReason = reason?.Trim();
        CancelledAt = DateTime.UtcNow;
    }

    /// <summary>Called only by the background expiry job — never from a user-facing endpoint.</summary>
    public void MarkExpired()
    {
        EnsureStatus(BookingStatus.Pending, "انتهاء صلاحية");
        Status = BookingStatus.Expired;
    }

    /// <summary>Called only by the background expiry job — never from a user-facing endpoint.</summary>
    public void MarkNoShow()
    {
        EnsureStatus(BookingStatus.Confirmed, "تسجيل عدم حضور");
        Status = BookingStatus.NoShow;
    }

    private void EnsureStatus(BookingStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidStateTransitionException($"لا يمكن {action} حجز في الحالة '{Status}'.");
    }
}
