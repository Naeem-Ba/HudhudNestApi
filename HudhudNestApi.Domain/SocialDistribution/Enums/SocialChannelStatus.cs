namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.SocialChannel"/>. A channel is never hard-deleted (it may
/// be referenced by historical publications through its accounts) — Inactive/Deprecated are the
/// only ways to stop using one.
/// </summary>
public enum SocialChannelStatus
{
    Active = 1,
    Inactive = 2,

    /// <summary>
    /// Terminal: this platform integration is retired for good (e.g. the platform shut down its
    /// public API). Unlike Inactive, a deprecated channel can never be reactivated — see
    /// <see cref="Entities.SocialChannel.Activate"/>.
    /// </summary>
    Deprecated = 3,
}
