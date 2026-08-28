using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Services.Enums;

namespace PropertyApi.Domain.Services.Entities;

/// <summary>
/// A business/professional offering one or more ServiceOfferings on the marketplace (e.g. the
/// AqarTech Verify team). One UserAccount runs at most one ServiceProvider profile.
///
/// AgencyId here is deliberately its own independent field — NOT read transitively from
/// UserAccount.AgencyId. A real-estate agency membership and "runs a verification/valuation
/// business" are unrelated facts about a person; wiring one to silently follow the other would
/// mean leaving one agency also silently detaches a service business that has nothing to do
/// with it. If a provider profile needs to be associated with an agency, that is set here
/// explicitly, once, by whoever creates the provider profile.
///
/// A provider does not declare which categories it serves directly — that is derived from its
/// active ServiceOfferings. Avoids a second place that can drift out of sync with what the
/// provider is actually offering.
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

    /// <summary>Whether the provider is publicly listed and can receive new requests.</summary>
    public bool IsActive { get; private set; } = true;

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

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }
}
