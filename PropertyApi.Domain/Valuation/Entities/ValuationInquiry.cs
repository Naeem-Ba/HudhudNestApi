using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Domain.Valuation.Entities;

/// <summary>
/// A user's (or an anonymous visitor's) request for an estimate of what their property is
/// worth. The root of the Valuation module: later phases match it against existing listings
/// and/or invite agencies (<see cref="ValuationOfficeInvitation"/>) to respond
/// (<see cref="ValuationOfficeResponse"/>) — none of that matching/invitation logic lives here;
/// this entity only describes the property being valued and tracks the inquiry's own
/// lifecycle.
///
/// DDD: private setters + factory method + domain state-machine methods, same shape as
/// ServiceRequest/AgencyInvitation. Deliberately plain <see cref="BaseEntity"/> rather than
/// AuditableEntity — there is no "who created/updated/deleted this" concept beyond
/// <see cref="RequesterId"/> itself (which is already nullable for the guest case), so the
/// extra *ByUserId columns AuditableEntity adds would just sit unused (same reasoning
/// PropertyShareEvent and AgencyInvitation already apply to their own BaseEntity choice).
/// </summary>
public sealed class ValuationInquiry : BaseEntity
{
    /// <summary>How long an inquiry stays actionable after creation.</summary>
    public static readonly TimeSpan DefaultExpiryWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Remediation M4 — how long after creation an unresolved inquiry gets a "closing soon"
    /// reminder, i.e. CreatedAt + ReminderWindow. Deliberately expressed as an offset from
    /// CreatedAt (not from ExpiresAt) so it stays correct even though ExpiresAt is itself just
    /// CreatedAt + DefaultExpiryWindow — one fixed reference point, same as ExpiresAt's own
    /// computation in <see cref="Create"/>.
    /// </summary>
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(18);

    private ValuationInquiry() { }

    /// <summary>
    /// The requesting user, or null for an anonymous visitor. Nullable by design — same
    /// pattern PropertyShareEvent.UserId already uses for "may or may not be logged in":
    /// no separate guest-account concept is invented here, and none is needed for a
    /// Domain-only inquiry that just describes a property.
    /// </summary>
    public Guid? RequesterId { get; private set; }

    /// <summary>
    /// FK-shaped reference to the PropertyTypes lookup (Domain.Lookups.Entities.PropertyType),
    /// exactly like Property.PropertyTypeId — reused rather than inventing a
    /// ValuationPropertyType. Optional, matching Property's own nullability; this phase adds
    /// no navigation property (see the module's design note — Domain-only, no cross-module
    /// object graph yet).
    /// </summary>
    public int? PropertyTypeId { get; private set; }

    /// <summary>Same type and "null or positive" rule as Property.Area (decimal, nullable).</summary>
    public decimal? Area { get; private set; }

    /// <summary>Same type and "null or positive" rule as Property.Rooms (int, nullable).</summary>
    public int? Rooms { get; private set; }

    /// <summary>
    /// Governorate (FK → Lookups.Governorate). The one mandatory location field — an inquiry
    /// cannot be created without it (see <see cref="Create"/>), unlike Property/Agency where
    /// it is nullable for backward compatibility with pre-existing rows. There is no such
    /// legacy data to be compatible with here: this is a brand-new entity.
    /// </summary>
    public int GovernorateId { get; private set; }

    /// <summary>Optional, more precise than Governorate. Must belong to GovernorateId — that
    /// cross-table check needs a lookup and is deferred to the Application layer (section 10);
    /// this layer only guards what is checkable without one (a positive id).</summary>
    public int? DistrictId { get; private set; }

    /// <summary>
    /// Optional, most precise. Locally checkable invariant: a neighborhood cannot be given
    /// without its district (same "child requires its parent" shape as District/Neighborhood
    /// elsewhere in this codebase), enforced in <see cref="Create"/> without any database
    /// access — whether it belongs to *this* district is, again, an Application-layer check.
    /// </summary>
    public int? NeighborhoodId { get; private set; }

    /// <summary>
    /// Sale or rent. Reuses <see cref="ListingType"/> (Property's own listing-type enum)
    /// instead of inventing a duplicate Sale/Rent concept — ForRentAndSale is also accepted
    /// (an owner may genuinely want an estimate covering both), nothing here restricts it to
    /// only two of its three values.
    /// </summary>
    public ListingType RequestType { get; private set; }

    public ValuationInquiryStatus Status { get; private set; } = ValuationInquiryStatus.Pending;

    /// <summary>
    /// Always exactly CreatedAt + <see cref="DefaultExpiryWindow"/> (24h) — never supplied by
    /// the caller, computed in <see cref="Create"/> the same way AgencyInvitation.ExpiresAt is
    /// computed from its own CreatedAt/SentAt.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Remediation M3 — stamped only once <see cref="Notifications.Interfaces.INotificationService.NotifyValuationInquiryExpiredAsync"/>
    /// actually succeeds for this inquiry (never just because <see cref="Expire"/> ran). Null
    /// means "Expired, but the notification has not yet been confirmed delivered" — the signal
    /// ValuationSlaEnforcementService's own retry pass uses to find and retry rows whose first
    /// attempt failed, mirroring Property.ExpiryWarningSentAt's own "idempotency stamp, set on
    /// success only" pattern. Same "guest inquiry has no account to notify" rule as
    /// RequesterId's own nullability — an anonymous inquiry never needs this stamped at all.
    /// </summary>
    public DateTime? ExpiryNotifiedAt { get; private set; }

    /// <summary>
    /// Remediation M3/H2 — same idempotency-stamp pattern as <see cref="ExpiryNotifiedAt"/>,
    /// for <see cref="Notifications.Interfaces.INotificationService.NotifyValuationResultReadyAsync"/>
    /// once this inquiry reaches <see cref="ValuationInquiryStatus.Completed"/> via
    /// SubmitOfficeResponseCommandHandler.
    /// </summary>
    public DateTime? ResultReadyNotifiedAt { get; private set; }

    /// <summary>
    /// Remediation M4 — stamped only once the CreatedAt+18h "closing soon" reminder has
    /// actually been delivered successfully for this inquiry, so ten sweep ticks after the 18h
    /// mark produce exactly one reminder, not ten — same "idempotency stamp, set on success
    /// only" pattern as <see cref="ExpiryNotifiedAt"/>/<see cref="ResultReadyNotifiedAt"/>
    /// above (and Property.ExpiryWarningSentAt), so a transient failure is retried on the next
    /// sweep tick instead of the reminder being silently skipped forever.
    /// </summary>
    public DateTime? ReminderSentAt { get; private set; }

    public static ValuationInquiry Create(
        int governorateId,
        ListingType requestType,
        DateTime utcNow,
        Guid? requesterId = null,
        int? propertyTypeId = null,
        decimal? area = null,
        int? rooms = null,
        int? districtId = null,
        int? neighborhoodId = null)
    {
        if (governorateId <= 0)
            throw new DomainException("لا يمكن إنشاء طلب تقييم بلا محافظة.");

        if (districtId is { } d && d <= 0)
            throw new DomainException("معرّف المنطقة غير صالح.");

        if (neighborhoodId is { } n && n <= 0)
            throw new DomainException("معرّف الحي غير صالح.");

        // A neighborhood cannot stand alone — it must be anchored under a district, same
        // "child requires parent" shape District/Neighborhood already have on Property/Agency.
        // This is the one part of the hierarchy this layer CAN verify without a database
        // lookup (it's pure presence, not "does it actually belong to this district").
        if (neighborhoodId.HasValue && !districtId.HasValue)
            throw new DomainException("لا يمكن تحديد حي بلا منطقة.");

        if (propertyTypeId is { } pt && pt <= 0)
            throw new DomainException("معرّف نوع العقار غير صالح.");

        // "null or positive" — the exact rule Property enforces for Area/Rooms via its
        // CK_Properties_Area_PositiveOrNull / CK_Properties_Rooms_PositiveOrNull check
        // constraints, just expressed here as a Domain invariant instead of a DB constraint.
        if (area is { } a && a <= 0)
            throw new DomainException("مساحة العقار يجب أن تكون أكبر من صفر.");

        if (rooms is { } r && r <= 0)
            throw new DomainException("عدد الغرف يجب أن يكون أكبر من صفر.");

        if (!Enum.IsDefined(requestType))
            throw new DomainException("نوع الطلب غير معروف.");

        if (requesterId is { } uid && uid == Guid.Empty)
            throw new DomainException("معرّف المستخدم الطالب غير صالح.");

        return new ValuationInquiry
        {
            RequesterId = requesterId,
            PropertyTypeId = propertyTypeId,
            Area = area,
            Rooms = rooms,
            GovernorateId = governorateId,
            DistrictId = districtId,
            NeighborhoodId = neighborhoodId,
            RequestType = requestType,
            Status = ValuationInquiryStatus.Pending,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
            ExpiresAt = utcNow.Add(DefaultExpiryWindow),
        };
    }

    // ── Domain State-Machine ──────────────────────────────────────
    //
    // Pending is the only real starting gate. MatchedFromListings and
    // AwaitingOfficeResponses are both "in progress" — nothing in this phase's spec commits
    // to one being a mandatory prerequisite of the other (an inquiry might go straight from
    // Pending to inviting offices without ever getting a listing match), so
    // MarkAwaitingOfficeResponses accepts either. Completed requires some matching work to
    // have actually happened first — jumping straight from Pending to Completed would mean
    // "valued" without ever having matched anything, which is not a real completion.

    public bool IsExpired(DateTime utcNow)
        => Status is ValuationInquiryStatus.Pending
            or ValuationInquiryStatus.MatchedFromListings
            or ValuationInquiryStatus.AwaitingOfficeResponses
        && utcNow >= ExpiresAt;

    public void MarkMatchedFromListings(DateTime utcNow)
    {
        EnsureStatus(ValuationInquiryStatus.Pending, "مطابقة الإعلانات");
        Status = ValuationInquiryStatus.MatchedFromListings;
        UpdatedAt = utcNow;
    }

    public void MarkAwaitingOfficeResponses(DateTime utcNow)
    {
        if (Status is not (ValuationInquiryStatus.Pending or ValuationInquiryStatus.MatchedFromListings))
        {
            throw new DomainException(
                $"لا يمكن الانتقال إلى انتظار ردود المكاتب من الحالة '{Status}'.");
        }

        Status = ValuationInquiryStatus.AwaitingOfficeResponses;
        UpdatedAt = utcNow;
    }

    public void MarkCompleted(DateTime utcNow)
    {
        if (Status is not (ValuationInquiryStatus.MatchedFromListings or ValuationInquiryStatus.AwaitingOfficeResponses))
        {
            throw new DomainException(
                $"لا يمكن إكمال طلب تقييم في الحالة '{Status}'.");
        }

        Status = ValuationInquiryStatus.Completed;
        UpdatedAt = utcNow;
    }

    public void Expire(DateTime utcNow)
    {
        if (Status is ValuationInquiryStatus.Completed or ValuationInquiryStatus.Expired)
            throw new DomainException($"لا يمكن انتهاء صلاحية طلب تقييم بحالة '{Status}'.");

        Status = ValuationInquiryStatus.Expired;
        UpdatedAt = utcNow;
    }

    private void EnsureStatus(ValuationInquiryStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainException($"لا يمكن {action} لطلب تقييم في الحالة '{Status}'.");
    }

    /// <summary>Remediation M3 — marks the ValuationInquiryExpired notification as
    /// successfully delivered. No status guard, same as Property.MarkExpiryWarningSent — a
    /// plain idempotency stamp, not a state transition.</summary>
    public void MarkExpiryNotified(DateTime utcNow)
    {
        ExpiryNotifiedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>Remediation M3 — marks the ValuationResultReady notification as successfully
    /// delivered.</summary>
    public void MarkResultReadyNotified(DateTime utcNow)
    {
        ResultReadyNotifiedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>Remediation M4 — marks the CreatedAt+18h reminder as successfully
    /// delivered.</summary>
    public void MarkReminderSent(DateTime utcNow)
    {
        ReminderSentAt = utcNow;
        UpdatedAt = utcNow;
    }
}
