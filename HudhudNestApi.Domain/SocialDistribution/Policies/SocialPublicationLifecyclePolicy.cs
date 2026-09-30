using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Policies;

/// <summary>
/// The single, named place that decides what a Property status transition means for an already-
/// live <see cref="Entities.SocialPublication"/> (Phase 11 spec §7: "لا تضع هذه السياسة داخل
/// if/else عشوائي في Handler"). Pure and static — same centralization pattern this codebase
/// already uses for <see cref="SocialContentPolicy"/> and <see cref="SocialAssetPresetCatalog"/>:
/// one dictionary that both a handler and a unit test read from, instead of scattered
/// property-status conditionals duplicated across call sites.
///
/// Depends only on <see cref="PropertyStatus"/> (a plain shared enum, not the Property entity or
/// anything else from the Listings bounded context) — SocialDistribution still never references
/// Property itself, matching every other cross-context reference in this bounded context (see
/// <see cref="Entities.SocialPublication.PropertyId"/>).
/// </summary>
public static class SocialPublicationLifecyclePolicy
{
    private static readonly Dictionary<(PropertyStatus From, PropertyStatus To), SocialPublicationLifecycleAction> Transitions = new()
    {
        // Available → Reserved: the listing is still real, just spoken for — a comment is enough,
        // the original post (with its real facts) should not be rewritten.
        [(PropertyStatus.Available, PropertyStatus.Reserved)] = SocialPublicationLifecycleAction.Comment,

        // A reservation falling through — worth a comment too (spec explicitly leaves this
        // transition open to policy, not silence).
        [(PropertyStatus.Reserved, PropertyStatus.Available)] = SocialPublicationLifecycleAction.Comment,

        // Reserved/Available → Sold or Rented: the listing is no longer actionable — the post
        // itself should be updated to say so (spec §7 example).
        [(PropertyStatus.Reserved, PropertyStatus.Sold)] = SocialPublicationLifecycleAction.Update,
        [(PropertyStatus.Available, PropertyStatus.Sold)] = SocialPublicationLifecycleAction.Update,
        [(PropertyStatus.Reserved, PropertyStatus.Rented)] = SocialPublicationLifecycleAction.Update,
        [(PropertyStatus.Available, PropertyStatus.Rented)] = SocialPublicationLifecycleAction.Update,

        // The listing left the platform outright — remove the live post rather than leave a stale
        // one pointing at a delisted/expired property.
        [(PropertyStatus.Available, PropertyStatus.Archived)] = SocialPublicationLifecycleAction.Delete,
        [(PropertyStatus.Reserved, PropertyStatus.Archived)] = SocialPublicationLifecycleAction.Delete,
        [(PropertyStatus.Available, PropertyStatus.Expired)] = SocialPublicationLifecycleAction.Delete,
        [(PropertyStatus.Reserved, PropertyStatus.Expired)] = SocialPublicationLifecycleAction.Delete,
    };

    /// <summary>Any transition not listed above (including Sold/Rented/Archived/Expired → anything, which are terminal for this policy) defaults to <see cref="SocialPublicationLifecycleAction.NoOp"/> — never assumed.</summary>
    public static SocialPublicationLifecycleAction Resolve(PropertyStatus previousStatus, PropertyStatus newStatus) =>
        Transitions.TryGetValue((previousStatus, newStatus), out var action) ? action : SocialPublicationLifecycleAction.NoOp;
}
