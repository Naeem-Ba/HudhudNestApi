using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Entities;

/// <summary>
/// An actual page/profile/channel HudhudNest operates on one platform (spec §6.2) — e.g. "هدهد
/// نيست دمشق" on Facebook. Not to be confused with Auth's social-LOGIN concept
/// (<c>ISocialLoginIdentityService</c>/<c>SocialAccountMutationCoordinator</c> under
/// <c>PropertyApi.Application.Auth</c>): that is a visitor signing into HudhudNest using their own
/// Google/Facebook identity; this is HudhudNest posting content to an account it operates. Same
/// English words, disjoint bounded contexts, never referenced from one another.
///
/// Never stores a raw access/refresh token. <see cref="CredentialReference"/> is an opaque
/// string (in practice: a secret-manager key name a future real OAuth integration would look
/// the actual token up by) and is encrypted at rest regardless, via the same ASP.NET Core
/// Data-Protection column converter already used for WhatsAppNumber/TaxNumber
/// (see AppDbContext.ConfigureEncryptedSocialAccountFields) — defense in depth in case an
/// operator ever pastes something more sensitive than a reference into it by mistake.
/// </summary>
public sealed class SocialAccount : AuditableEntity
{
    private SocialAccount() { }

    public Guid SocialChannelId { get; private set; }

    /// <summary>Denormalized from the owning channel at creation time so a content-platform match
    /// (spec invariant §9.5) can be checked without an extra join/include.</summary>
    public SocialPlatform Platform { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The platform's own id for this page/profile/channel. Unique per platform.</summary>
    public string ExternalAccountId { get; private set; } = string.Empty;

    public SocialAccountStatus Status { get; private set; } = SocialAccountStatus.PendingAuthorization;

    public SocialAccountType AccountType { get; private set; }

    /// <summary>
    /// Optional — this account serves one governorate rather than the whole country (spec's
    /// "دعم حسابات حسب المحافظة"). Reuses <c>Property.GovernorateId</c>'s lookup table rather than
    /// inventing a parallel "Province" concept — same physical divisions, same FK target.
    /// </summary>
    public int? GovernorateId { get; private set; }

    /// <summary>Opaque reference to wherever the real credential lives — never the credential itself. See class remarks.</summary>
    public string? CredentialReference { get; private set; }

    public DateTime? ConnectedAt { get; private set; }

    public DateTime? DisconnectedAt { get; private set; }

    public static SocialAccount Create(
        Guid socialChannelId,
        SocialPlatform platform,
        string displayName,
        string externalAccountId,
        SocialAccountType accountType,
        int? governorateId = null)
    {
        if (socialChannelId == Guid.Empty)
            throw new DomainException("القناة الاجتماعية مطلوبة.");

        if (!Enum.IsDefined(platform))
            throw new DomainException("منصة اجتماعية غير معروفة.");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("اسم الحساب مطلوب.");

        if (string.IsNullOrWhiteSpace(externalAccountId))
            throw new DomainException("معرّف الحساب الخارجي مطلوب.");

        if (!Enum.IsDefined(accountType))
            throw new DomainException("نوع حساب غير معروف.");

        return new SocialAccount
        {
            SocialChannelId = socialChannelId,
            Platform = platform,
            DisplayName = displayName.Trim(),
            ExternalAccountId = externalAccountId.Trim(),
            AccountType = accountType,
            GovernorateId = governorateId,
        };
    }

    /// <summary>
    /// Establishes/refreshes the platform credential and moves the account to Active. Valid from
    /// any non-terminal-looking state — this is also how a Disconnected/Expired account is
    /// reconnected after re-authorization, which is the whole point of keeping those states
    /// distinct from a hard delete.
    /// </summary>
    public void Connect(string? credentialReference)
    {
        if (Status == SocialAccountStatus.Suspended)
            throw new InvalidStateTransitionException("لا يمكن ربط حساب أوقفته المنصة (Suspended) دون تدخل يدوي.");

        CredentialReference = string.IsNullOrWhiteSpace(credentialReference) ? null : credentialReference.Trim();
        Status = SocialAccountStatus.Active;
        ConnectedAt = DateTime.UtcNow;
        DisconnectedAt = null;
    }

    /// <summary>Revokes the credential. The account row (and its publication history) is kept — never deleted.</summary>
    public void Disconnect()
    {
        CredentialReference = null;
        Status = SocialAccountStatus.Disconnected;
        DisconnectedAt = DateTime.UtcNow;
    }

    public void Suspend() => Status = SocialAccountStatus.Suspended;

    public void MarkExpired() => Status = SocialAccountStatus.Expired;

    /// <summary>Administrative on/off switch, independent of the credential itself (spec: Active/Inactive vs. Disconnected).</summary>
    public void Deactivate()
    {
        if (Status == SocialAccountStatus.Active)
            Status = SocialAccountStatus.Inactive;
    }

    public void Reactivate()
    {
        if (Status == SocialAccountStatus.Inactive)
            Status = SocialAccountStatus.Active;
    }

    /// <summary>
    /// The single gate every publish attempt must pass (spec invariant §9.2): only a fully
    /// Active account may be the target of a new or executing SocialPublication.
    /// </summary>
    public bool CanPublish() => Status == SocialAccountStatus.Active;
}
