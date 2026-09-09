using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Entities;

/// <summary>
/// A social media platform AqarTech supports distributing to (spec §6.1) — e.g. "Facebook is a
/// platform we can post to". Deliberately NOT the same thing as an actual page/profile: that is
/// <see cref="SocialAccount"/>. One channel can back many accounts (a Facebook channel with a
/// Damascus page, an Aleppo page, ...).
///
/// Design decision: exactly ONE channel per <see cref="SocialPlatform"/> (enforced by a unique
/// index in <c>SocialChannelConfiguration</c> and by
/// <c>CreateSocialChannelCommandHandler</c> checking first) — a platform either is or isn't
/// wired into the distribution engine; "more than one Facebook channel" has no meaning distinct
/// from "more than one Facebook account", which SocialAccount already supports. This keeps the
/// spec's "دعم أكثر من حساب لكل منصة" requirement satisfied at the SocialAccount level without
/// duplicating platform-level configuration.
///
/// Never hard-deleted: a channel referenced by historical accounts/publications must stay
/// resolvable for reporting even after the platform integration is retired — see
/// <see cref="Deprecate"/>.
/// </summary>
public sealed class SocialChannel : BaseEntity
{
    private SocialChannel() { }

    public SocialPlatform Platform { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public SocialChannelStatus Status { get; private set; } = SocialChannelStatus.Active;

    /// <summary>
    /// Opaque version marker for whatever platform-specific configuration (API version, webhook
    /// schema, ...) a future real integration attaches to this channel. Unused today — no real
    /// integration exists yet (see docs) — kept nullable so it costs nothing until then.
    /// </summary>
    public string? ConfigurationVersion { get; private set; }

    public static SocialChannel Create(SocialPlatform platform, string name, string? configurationVersion = null)
    {
        if (!Enum.IsDefined(platform))
            throw new DomainException("منصة اجتماعية غير معروفة.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("اسم القناة مطلوب.");

        return new SocialChannel
        {
            Platform = platform,
            Name = name.Trim(),
            ConfigurationVersion = string.IsNullOrWhiteSpace(configurationVersion) ? null : configurationVersion.Trim(),
        };
    }

    /// <summary>Inactive → Active only. A Deprecated channel can never come back — see class remarks.</summary>
    public void Activate()
    {
        if (Status == SocialChannelStatus.Deprecated)
            throw new InvalidStateTransitionException("لا يمكن تفعيل قناة تم إيقافها نهائياً (Deprecated).");

        Status = SocialChannelStatus.Active;
    }

    public void Deactivate()
    {
        if (Status == SocialChannelStatus.Deprecated)
            throw new InvalidStateTransitionException("لا يمكن تعطيل قناة تم إيقافها نهائياً (Deprecated).");

        Status = SocialChannelStatus.Inactive;
    }

    /// <summary>Terminal — retires the channel for good (e.g. the platform shut its API down).</summary>
    public void Deprecate() => Status = SocialChannelStatus.Deprecated;

    public bool CanBackNewAccounts() => Status == SocialChannelStatus.Active;
}
