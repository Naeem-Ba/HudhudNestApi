using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Domain.Services.Entities;

/// <summary>
/// A business/professional offering one or more ServiceOfferings on the marketplace (e.g. the
/// HudhudNest Verify team). One UserAccount runs at most one ServiceProvider profile.
///
/// AgencyId here is deliberately its own independent field — NOT read transitively from
/// UserAccount.AgencyId. A real-estate agency membership and "runs a verification/valuation
/// business" are unrelated facts about a person; wiring one to silently follow the other would
/// mean leaving one agency also silently detaches a service business that has nothing to do
/// with it. If a provider profile needs to be associated with an agency, that is set here
/// explicitly, once, by whoever creates the provider profile.
///
/// <see cref="ApprovedCategories"/> and <see cref="CoverageGovernorateIds"/> are what an admin
/// APPROVED, not what the provider currently sells: a provider may only publish offerings in an
/// approved category, and only offerings whose category is still approved are shown publicly.
/// What it actually sells is still its active ServiceOfferings.
///
/// <see cref="Status"/> is the provider's standing today (see <see cref="ServiceProviderStatus"/>);
/// <see cref="IsActive"/> is kept as its stored, query-friendly shadow (Status == Active) and is
/// only ever written by the methods below, so the two cannot disagree.
/// </summary>
public sealed class ServiceProvider : AuditableEntity
{
    private ServiceProvider() { }

    public Guid UserId { get; private set; }

    /// <summary>Optional real-estate agency this provider is run under. See class remarks.</summary>
    public Guid? AgencyId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? Bio { get; private set; }

    public string? LogoUrl { get; private set; }

    /// <summary>Cloudinary PublicId — same {Url,PublicId} pairing used by Agency/UserAccount.</summary>
    public string? LogoPublicId { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    /// <summary>
    /// What the platform has checked about this provider. See
    /// <see cref="ServiceProviderVerificationLevel"/> — never a bare boolean, and never implies
    /// anything about a property or a document being genuine.
    /// </summary>
    public ServiceProviderVerificationLevel VerificationLevel { get; private set; } =
        ServiceProviderVerificationLevel.None;

    /// <summary>Whether the provider is publicly listed and can receive new requests (Status == Active).</summary>
    public bool IsActive { get; private set; } = true;

    public ServiceProviderStatus Status { get; private set; } = ServiceProviderStatus.Active;

    /// <summary>Why the provider was suspended or revoked. Cleared on reactivation.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>ServiceCategory names an admin approved (see <see cref="ServiceCategorySet"/>).</summary>
    public string[] ApprovedCategories { get; private set; } = [];

    /// <summary>
    /// Governorate ids the provider is approved to serve. Empty means no restriction was ever
    /// recorded (profiles created before approvals existed); every approval sets at least one.
    /// </summary>
    public int[] CoverageGovernorateIds { get; private set; } = [];

    public bool IsApprovedFor(ServiceCategory category) =>
        ApprovedCategories.Contains(category.ToString());

    public bool Covers(int? governorateId) =>
        governorateId is null || CoverageGovernorateIds.Length == 0 || CoverageGovernorateIds.Contains(governorateId.Value);

    public static ServiceProvider Create(
        Guid userId,
        Guid? agencyId,
        string displayName,
        string? bio,
        string? contactEmail,
        string? contactPhone,
        DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء مزوّد خدمة بلا مستخدم مالك.");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("اسم مزوّد الخدمة مطلوب.");

        return new ServiceProvider
        {
            UserId = userId,
            AgencyId = agencyId,
            DisplayName = displayName.Trim(),
            Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim(),
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    public void UpdateProfile(
        string displayName,
        string? bio,
        string? contactEmail,
        string? contactPhone,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("اسم مزوّد الخدمة مطلوب.");

        DisplayName = displayName.Trim();
        Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
        UpdatedAt = utcNow;
    }

    public void SetLogo(string? logoUrl, string? logoPublicId, DateTime utcNow)
    {
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        LogoPublicId = string.IsNullOrWhiteSpace(logoPublicId) ? null : logoPublicId.Trim();
        UpdatedAt = utcNow;
    }

    /// <summary>Admin-only — see SetServiceProviderVerifiedCommand.</summary>
    public void SetVerificationLevel(ServiceProviderVerificationLevel level, DateTime utcNow)
    {
        VerificationLevel = level;
        UpdatedAt = utcNow;
    }

    /// <summary>Admin-approved scope. Categories may be empty only for a legacy profile; approvals require one.</summary>
    public void SetApprovedScope(
        IEnumerable<ServiceCategory> categories, IEnumerable<int> governorateIds, DateTime utcNow)
    {
        var names = ServiceCategorySet.ToNames(categories);
        var governorates = ServiceCategorySet.NormalizeIds(governorateIds);

        if (names.Length == 0)
            throw new DomainException("اختر نوع خدمة معتمدًا واحدًا على الأقل.");

        if (governorates.Length == 0)
            throw new DomainException("اختر محافظة واحدة على الأقل لنطاق التغطية.");

        ApprovedCategories = names;
        CoverageGovernorateIds = governorates;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Approval of a (new) application for this profile: back to Active, whatever it was. A
    /// Revoked profile is re-activated here and only here.
    /// </summary>
    public void ActivateByApproval(DateTime utcNow)
    {
        Status = ServiceProviderStatus.Active;
        IsActive = true;
        StatusReason = null;
        UpdatedAt = utcNow;
    }

    public void Suspend(string reason, DateTime utcNow)
    {
        if (Status != ServiceProviderStatus.Active)
            throw new InvalidStateTransitionException("يمكن إيقاف المزوّد النشط فقط.");

        Status = ServiceProviderStatus.Suspended;
        IsActive = false;
        StatusReason = RequireReason(reason);
        UpdatedAt = utcNow;
    }

    public void Reactivate(DateTime utcNow)
    {
        if (Status != ServiceProviderStatus.Suspended)
            throw new InvalidStateTransitionException(
                "يمكن إعادة تفعيل المزوّد الموقوف فقط. المزوّد الملغى اعتماده يعود بطلب انضمام جديد.");

        Status = ServiceProviderStatus.Active;
        IsActive = true;
        StatusReason = null;
        UpdatedAt = utcNow;
    }

    public void Revoke(string reason, DateTime utcNow)
    {
        if (Status == ServiceProviderStatus.Revoked)
            throw new InvalidStateTransitionException("اعتماد هذا المزوّد ملغى بالفعل.");

        Status = ServiceProviderStatus.Revoked;
        IsActive = false;
        StatusReason = RequireReason(reason);
        UpdatedAt = utcNow;
    }

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("السبب مطلوب.");

        return reason.Trim();
    }
}
