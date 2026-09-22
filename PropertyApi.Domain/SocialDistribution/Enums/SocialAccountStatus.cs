namespace PropertyApi.Domain.SocialDistribution.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.SocialAccount"/>. Only <see cref="Active"/> accounts may
/// be used as the target of a new <see cref="Entities.SocialPublication"/> — see
/// <see cref="Entities.SocialAccount.CanPublish"/>.
/// </summary>
public enum SocialAccountStatus
{
    /// <summary>Created, but the OAuth/connect handshake with the platform has not completed yet.</summary>
    PendingAuthorization = 1,

    Active = 2,

    /// <summary>Administratively disabled by a HudhudNest operator, credentials untouched.</summary>
    Inactive = 3,

    /// <summary>The platform connection itself was revoked (token cleared) — see <see cref="Entities.SocialAccount.Disconnect"/>.</summary>
    Disconnected = 4,

    /// <summary>The platform suspended or restricted this account/page.</summary>
    Suspended = 5,

    /// <summary>The stored credential expired and needs to be re-authorized before publishing can resume.</summary>
    Expired = 6,
}
