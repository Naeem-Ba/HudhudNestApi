using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Domain.Marketing.Entities;

/// <summary>
/// A time- and/or quantity-bounded marketing offer, e.g. "the first 100 agencies get X% off
/// for Y months". Deliberately holds no reference to <c>Plan</c> billing or a payment
/// gateway (none exists yet — FRONTEND_BACKEND_CONTRACT.md §11.5): this is a lead-capture
/// commitment the sales team honors manually when a Lead converts, not something the
/// checkout flow applies automatically.
///
/// <see cref="RedeemedCount"/> is only ever incremented through
/// <c>IOfferRepository.TryReserveRedemptionAsync</c>, a single atomic
/// <c>UPDATE ... WHERE RedeemedCount &lt; MaxRedemptions</c> statement — never by loading
/// the entity, mutating it in memory, and calling SaveChanges — so two concurrent
/// submissions racing for the last slot cannot both succeed (RELEASE-BLOCKERS-AR.md B-10's
/// "read-then-write race" class of bug, closed here without needing an advisory lock).
/// </summary>
public sealed class Offer : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public OfferDiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }

    /// <summary>Which <c>Plan.Tier</c> this discount is meant for once billing exists.
    /// Null = not tied to a specific plan (e.g. a general early-access badge).</summary>
    public string? TargetPlanTier { get; private set; }

    public DateTime StartsAtUtc { get; private set; }

    /// <summary>Null = no fixed end date; the offer is bounded by
    /// <see cref="MaxRedemptions"/> alone (or ended manually).</summary>
    public DateTime? EndsAtUtc { get; private set; }

    /// <summary>Null = unlimited redemptions (bounded by date only, if at all).</summary>
    public int? MaxRedemptions { get; private set; }

    public int RedeemedCount { get; private set; }

    public OfferStatus Status { get; private set; } = OfferStatus.Draft;

    public string? Terms { get; private set; }

    private Offer() { }

    public static Offer Create(
        string name,
        OfferDiscountType discountType,
        decimal discountValue,
        DateTime startsAtUtc,
        string? description = null,
        string? targetPlanTier = null,
        DateTime? endsAtUtc = null,
        int? maxRedemptions = null,
        string? terms = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم العرض مطلوب.");

        if (discountValue <= 0)
            throw new DomainException("قيمة الخصم يجب أن تكون أكبر من صفر.");

        if (discountType == OfferDiscountType.Percentage && discountValue > 100)
            throw new DomainException("نسبة الخصم لا يمكن أن تتجاوز 100%.");

        if (endsAtUtc.HasValue && endsAtUtc.Value <= startsAtUtc)
            throw new DomainException("تاريخ انتهاء العرض يجب أن يكون بعد تاريخ بدايته.");

        if (maxRedemptions is <= 0)
            throw new DomainException("الحد الأقصى للمستخدمين يجب أن يكون أكبر من صفر، أو فارغًا لعدم وجود حد.");

        return new Offer
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DiscountType = discountType,
            DiscountValue = discountValue,
            TargetPlanTier = string.IsNullOrWhiteSpace(targetPlanTier)
                ? null
                : targetPlanTier.Trim().ToLowerInvariant(),
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            MaxRedemptions = maxRedemptions,
            RedeemedCount = 0,
            Status = OfferStatus.Draft,
            Terms = string.IsNullOrWhiteSpace(terms) ? null : terms.Trim()
        };
    }

    public void UpdateDetails(
        string name,
        OfferDiscountType discountType,
        decimal discountValue,
        DateTime startsAtUtc,
        string? description,
        string? targetPlanTier,
        DateTime? endsAtUtc,
        int? maxRedemptions,
        string? terms)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم العرض مطلوب.");

        if (discountValue <= 0)
            throw new DomainException("قيمة الخصم يجب أن تكون أكبر من صفر.");

        if (discountType == OfferDiscountType.Percentage && discountValue > 100)
            throw new DomainException("نسبة الخصم لا يمكن أن تتجاوز 100%.");

        if (endsAtUtc.HasValue && endsAtUtc.Value <= startsAtUtc)
            throw new DomainException("تاريخ انتهاء العرض يجب أن يكون بعد تاريخ بدايته.");

        // A running offer's cap may only move up, never below what has already been
        // redeemed — shrinking it under RedeemedCount would silently misrepresent an
        // offer visitors already legitimately claimed.
        if (maxRedemptions.HasValue && maxRedemptions.Value < RedeemedCount)
        {
            throw new DomainException(
                $"لا يمكن ضبط الحد الأقصى ({maxRedemptions}) أقل من عدد المستفيدين الحاليين ({RedeemedCount}).");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DiscountType = discountType;
        DiscountValue = discountValue;
        TargetPlanTier = string.IsNullOrWhiteSpace(targetPlanTier)
            ? null
            : targetPlanTier.Trim().ToLowerInvariant();
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        MaxRedemptions = maxRedemptions;
        Terms = string.IsNullOrWhiteSpace(terms) ? null : terms.Trim();
    }

    public void Activate()
    {
        if (Status == OfferStatus.Ended)
            throw new DomainException("لا يمكن إعادة تفعيل عرض منتهٍ.");

        Status = OfferStatus.Active;
    }

    public void Pause()
    {
        if (Status != OfferStatus.Active)
            throw new DomainException("لا يمكن إيقاف عرض غير مفعّل مؤقتًا إلا إذا كان نشطًا.");

        Status = OfferStatus.Paused;
    }

    public void End()
    {
        Status = OfferStatus.Ended;
    }

    /// <summary>
    /// Whether this offer would currently accept a new redemption, from the loaded
    /// snapshot's point of view. This is a display/UX check only — it tells the frontend
    /// whether to show the CTA at all. The actual guarantee against over-redemption is the
    /// atomic conditional UPDATE in <c>IOfferRepository.TryReserveRedemptionAsync</c>, which
    /// re-checks the same conditions at the database row level at reservation time.
    /// </summary>
    public bool IsCurrentlyRedeemable(DateTime nowUtc) =>
        Status == OfferStatus.Active &&
        nowUtc >= StartsAtUtc &&
        (!EndsAtUtc.HasValue || nowUtc <= EndsAtUtc.Value) &&
        (!MaxRedemptions.HasValue || RedeemedCount < MaxRedemptions.Value);
}
