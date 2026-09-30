using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialChannelTests
{
    [Fact]
    public void Create_ValidInput_StartsActive()
    {
        var channel = SocialChannel.Create(SocialPlatform.Facebook, "Facebook");

        Assert.Equal(SocialChannelStatus.Active, channel.Status);
        Assert.Equal("Facebook", channel.Name);
        Assert.Equal(SocialPlatform.Facebook, channel.Platform);
        Assert.True(channel.CanBackNewAccounts());
    }

    [Fact]
    public void Create_BlankName_Throws() =>
        Assert.Throws<DomainException>(() => SocialChannel.Create(SocialPlatform.Facebook, "  "));

    [Fact]
    public void Deactivate_ThenActivate_ReturnsToActive()
    {
        var channel = SocialChannel.Create(SocialPlatform.Telegram, "Telegram");

        channel.Deactivate();
        Assert.Equal(SocialChannelStatus.Inactive, channel.Status);
        Assert.False(channel.CanBackNewAccounts());

        channel.Activate();
        Assert.Equal(SocialChannelStatus.Active, channel.Status);
    }

    [Fact]
    public void Deprecate_IsTerminal_CannotBeActivatedAgain()
    {
        var channel = SocialChannel.Create(SocialPlatform.YouTube, "YouTube");

        channel.Deprecate();
        Assert.Equal(SocialChannelStatus.Deprecated, channel.Status);

        Assert.Throws<InvalidStateTransitionException>(() => channel.Activate());
        Assert.Throws<InvalidStateTransitionException>(() => channel.Deactivate());
    }
}
