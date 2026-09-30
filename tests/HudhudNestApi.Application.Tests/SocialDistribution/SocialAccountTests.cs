using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialAccountTests
{
    private static SocialAccount MakeAccount() => SocialAccount.Create(
        Guid.NewGuid(), SocialPlatform.Facebook, "هدهد نيست دمشق", "ext-123", SocialAccountType.Page, governorateId: 1);

    [Fact]
    public void Create_ValidInput_StartsPendingAuthorization_CannotPublishYet()
    {
        var account = MakeAccount();

        Assert.Equal(SocialAccountStatus.PendingAuthorization, account.Status);
        Assert.False(account.CanPublish());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankExternalAccountId_Throws(string externalId) =>
        Assert.Throws<DomainException>(() => SocialAccount.Create(
            Guid.NewGuid(), SocialPlatform.Facebook, "Name", externalId, SocialAccountType.Page));

    [Fact]
    public void Connect_SetsActiveAndConnectedAt()
    {
        var account = MakeAccount();

        account.Connect("secret-manager-ref-1");

        Assert.Equal(SocialAccountStatus.Active, account.Status);
        Assert.True(account.CanPublish());
        Assert.NotNull(account.ConnectedAt);
        Assert.Null(account.DisconnectedAt);
    }

    [Fact]
    public void Disconnect_ClearsCredentialAndCannotPublish()
    {
        var account = MakeAccount();
        account.Connect("secret-manager-ref-1");

        account.Disconnect();

        Assert.Equal(SocialAccountStatus.Disconnected, account.Status);
        Assert.False(account.CanPublish());
        Assert.NotNull(account.DisconnectedAt);
    }

    [Fact]
    public void Connect_WhenSuspended_Throws()
    {
        var account = MakeAccount();
        account.Connect(null);
        account.Suspend();

        Assert.Throws<InvalidStateTransitionException>(() => account.Connect("new-ref"));
    }

    [Fact]
    public void Deactivate_ThenReactivate_RoundTrips()
    {
        var account = MakeAccount();
        account.Connect(null);

        account.Deactivate();
        Assert.Equal(SocialAccountStatus.Inactive, account.Status);
        Assert.False(account.CanPublish());

        account.Reactivate();
        Assert.Equal(SocialAccountStatus.Active, account.Status);
        Assert.True(account.CanPublish());
    }

    [Fact]
    public void CanPublish_IsFalse_WhileSuspended()
    {
        var account = MakeAccount();
        account.Connect(null);
        account.Suspend();

        Assert.Equal(SocialAccountStatus.Suspended, account.Status);
        Assert.False(account.CanPublish());
    }

    [Fact]
    public void MarkExpired_CannotPublish()
    {
        var account = MakeAccount();
        account.Connect(null);
        account.MarkExpired();

        Assert.Equal(SocialAccountStatus.Expired, account.Status);
        Assert.False(account.CanPublish());
    }
}
