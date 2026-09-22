using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>Phase 11 spec §7 — the centralized status-transition → action table, tested directly with no publisher/handler involved.</summary>
public sealed class SocialPublicationLifecyclePolicyTests
{
    [Fact]
    public void Resolve_AvailableToReserved_IsComment() =>
        Assert.Equal(SocialPublicationLifecycleAction.Comment, SocialPublicationLifecyclePolicy.Resolve(PropertyStatus.Available, PropertyStatus.Reserved));

    [Fact]
    public void Resolve_ReservedToSold_IsUpdate() =>
        Assert.Equal(SocialPublicationLifecycleAction.Update, SocialPublicationLifecyclePolicy.Resolve(PropertyStatus.Reserved, PropertyStatus.Sold));

    [Fact]
    public void Resolve_AvailableToSold_IsUpdate() =>
        Assert.Equal(SocialPublicationLifecycleAction.Update, SocialPublicationLifecyclePolicy.Resolve(PropertyStatus.Available, PropertyStatus.Sold));

    [Fact]
    public void Resolve_AvailableToArchived_IsDelete() =>
        Assert.Equal(SocialPublicationLifecycleAction.Delete, SocialPublicationLifecyclePolicy.Resolve(PropertyStatus.Available, PropertyStatus.Archived));

    [Fact]
    public void Resolve_SoldToAnything_IsNoOp_TerminalForThisPolicy() =>
        Assert.Equal(SocialPublicationLifecycleAction.NoOp, SocialPublicationLifecyclePolicy.Resolve(PropertyStatus.Sold, PropertyStatus.Available));

    [Fact]
    public void Resolve_UnlistedTransition_DefaultsToNoOp_NeverAssumed() =>
        Assert.Equal(SocialPublicationLifecycleAction.NoOp, SocialPublicationLifecyclePolicy.Resolve(PropertyStatus.Expired, PropertyStatus.Expired));
}
