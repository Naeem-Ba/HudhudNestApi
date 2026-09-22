using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Entities;

/// <summary>
/// A manageable, data-driven rule that decides which <see cref="SocialAccount"/>(s) a published
/// property should be distributed to (Phase 4 spec §5-§11) — replaces any Hardcoded
/// "Province == X → Platform Y" branching in code. Every field except <see cref="SocialAccountId"/>
/// and <see cref="Priority"/> is nullable, and a null value is documented — never ambiguous
/// (spec §6): it means "matches every value of this dimension", not "matches none".
///
/// Design decision — rule targets a specific <see cref="SocialAccount"/> directly rather than a
/// <see cref="SocialChannel"/> plus an account-resolution strategy: every worked example in the
/// spec ("Province=Tartus → Facebook طرطوس") already names a specific account, not just a
/// platform, and a real operator managing rules from the admin panel is choosing "post to THIS
/// page", not "post to whichever Facebook page the system picks". Resolving a channel to one of
/// several candidate accounts would need its own tie-breaking policy with no natural, spec-given
/// default — direct targeting has none of that ambiguity and matches §7's examples exactly.
///
/// DDD: private setters + factory method + domain methods, mirroring every other aggregate in
/// this codebase.
/// </summary>
public sealed class DistributionRule : AuditableEntity
{
    private DistributionRule() { }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>FK → Governorates. Null = applies to every province (spec §6: "قاعدة عامة لجميع المحافظات").</summary>
    public int? ProvinceId { get; private set; }

    /// <summary>FK → PropertyTypes. Null = applies to every property type.</summary>
    public int? PropertyTypeId { get; private set; }

    /// <summary>Null = applies to every transaction type (rent/sale/rent-and-sale).</summary>
    public ListingType? TransactionType { get; private set; }

    /// <summary>The specific social account this rule distributes to. Always required — see class remarks.</summary>
    public Guid SocialAccountId { get; private set; }

    /// <summary>Explicit operator-assigned rank. Higher wins first when several active rules target the same account for the same property — see <see cref="Services.DistributionRuleEvaluator"/>.</summary>
    public int Priority { get; private set; }

    /// <summary>Administrative on/off switch — independent of <see cref="IsArchived"/> (spec: "تفعيل أو تعطيل القاعدة دون حذفها").</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Terminal soft-delete flag (spec §18: "حذف منطقي أو أرشفة قاعدة"). An archived rule can
    /// never be reactivated, updated, or matched again — distinct from <see cref="IsActive"/>
    /// so "temporarily off" and "retired for good" are never confused with each other, mirroring
    /// <see cref="SocialChannel"/>'s Inactive-vs-Deprecated distinction.
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>Optional validity window start. Null = no lower bound.</summary>
    public DateTime? StartAt { get; private set; }

    /// <summary>Optional validity window end. Null = no upper bound.</summary>
    public DateTime? EndAt { get; private set; }

    /// <summary>
    /// How many of the three narrowing dimensions (Province/PropertyType/TransactionType) this
    /// rule pins down. Used as the automatic tie-breaker beneath explicit <see cref="Priority"/>
    /// — see <see cref="Services.DistributionRuleEvaluator.SelectWinner"/> for the full ordering
    /// and its rationale.
    /// </summary>
    public int SpecificityScore =>
        (ProvinceId.HasValue ? 1 : 0) +
        (PropertyTypeId.HasValue ? 1 : 0) +
        (TransactionType.HasValue ? 1 : 0);

    public static DistributionRule Create(
        string name,
        string? description,
        int? provinceId,
        int? propertyTypeId,
        ListingType? transactionType,
        Guid socialAccountId,
        int priority,
        DateTime? startAt,
        DateTime? endAt,
        Guid? createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم القاعدة مطلوب.");

        if (socialAccountId == Guid.Empty)
            throw new DomainException("يجب تحديد الحساب الاجتماعي الذي تستهدفه القاعدة.");

        if (priority < 0)
            throw new DomainException("لا يمكن أن تكون أولوية القاعدة سالبة.");

        if (provinceId is <= 0)
            throw new DomainException("معرّف المحافظة غير صالح.");

        if (propertyTypeId is <= 0)
            throw new DomainException("معرّف نوع العقار غير صالح.");

        if (startAt is not null && endAt is not null && startAt >= endAt)
            throw new DomainException("تاريخ بدء القاعدة يجب أن يسبق تاريخ انتهائها.");

        return new DistributionRule
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ProvinceId = provinceId,
            PropertyTypeId = propertyTypeId,
            TransactionType = transactionType,
            SocialAccountId = socialAccountId,
            Priority = priority,
            StartAt = startAt,
            EndAt = endAt,
            CreatedByUserId = createdByUserId,
        };
    }

    /// <summary>Updates the rule's matching criteria/priority/validity window in place. Rejected once archived.</summary>
    public void Update(
        string name,
        string? description,
        int? provinceId,
        int? propertyTypeId,
        ListingType? transactionType,
        int priority,
        DateTime? startAt,
        DateTime? endAt,
        Guid? updatedByUserId)
    {
        EnsureNotArchived("تعديل القاعدة");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم القاعدة مطلوب.");

        if (priority < 0)
            throw new DomainException("لا يمكن أن تكون أولوية القاعدة سالبة.");

        if (provinceId is <= 0)
            throw new DomainException("معرّف المحافظة غير صالح.");

        if (propertyTypeId is <= 0)
            throw new DomainException("معرّف نوع العقار غير صالح.");

        if (startAt is not null && endAt is not null && startAt >= endAt)
            throw new DomainException("تاريخ بدء القاعدة يجب أن يسبق تاريخ انتهائها.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ProvinceId = provinceId;
        PropertyTypeId = propertyTypeId;
        TransactionType = transactionType;
        Priority = priority;
        StartAt = startAt;
        EndAt = endAt;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        EnsureNotArchived("تفعيل القاعدة");
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        EnsureNotArchived("تعطيل القاعدة");
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Terminal. The rule row is kept (never hard-deleted) so historical Publications can still be traced back to it — see SocialPublication.DistributionRuleId.</summary>
    public void Archive(Guid? archivedByUserId)
    {
        IsArchived = true;
        IsActive = false;
        UpdatedByUserId = archivedByUserId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>True when <paramref name="utcNow"/> falls inside [StartAt, EndAt] (either bound may be open). Does NOT consider <see cref="IsActive"/>/<see cref="IsArchived"/> — see <see cref="Services.DistributionRuleEvaluator.Matches"/> for the full gate.</summary>
    public bool IsWithinValidityWindow(DateTime utcNow) =>
        (StartAt is null || StartAt <= utcNow) && (EndAt is null || EndAt >= utcNow);

    private void EnsureNotArchived(string action)
    {
        if (IsArchived)
            throw new InvalidStateTransitionException($"لا يمكن '{action}' لقاعدة توزيع مؤرشفة.");
    }
}
