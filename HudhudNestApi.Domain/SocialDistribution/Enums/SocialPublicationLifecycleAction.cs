namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>
/// What (if anything) should happen to an already-Published <see cref="Entities.SocialPublication"/>
/// when the underlying property's status changes (Phase 11 spec §7). Never assumed to be
/// achievable — the caller must still check the target platform's
/// <see cref="Models.SocialPublisherCapabilities"/> before attempting <see cref="Update"/>,
/// <see cref="Comment"/>, or <see cref="Delete"/>, and must fall back to <see cref="NoOp"/> when
/// the platform cannot do it (spec: "لا تفترض أن كل منصة تدعم نفس العمليات").
/// </summary>
public enum SocialPublicationLifecycleAction
{
    /// <summary>Do nothing to the live post — the transition does not warrant touching it.</summary>
    NoOp,

    /// <summary>Rewrite the post's text/image to reflect the new status (e.g. "تم البيع").</summary>
    Update,

    /// <summary>Leave the original post untouched, add a status comment beneath it.</summary>
    Comment,

    /// <summary>Remove the post from the platform entirely.</summary>
    Delete,
}
