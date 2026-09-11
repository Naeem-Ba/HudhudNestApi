using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using Xunit;

namespace PropertyApi.Application.Tests.Valuation;

public sealed class ValuationOfficeInvitationTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var agencyId = Guid.NewGuid();
        var inquiryId = Guid.NewGuid();

        var invitation = ValuationOfficeInvitation.Create(
            agencyId, inquiryId, ValuationMatchLevel.District, UtcNow);

        Assert.Equal(agencyId, invitation.AgencyId);
        Assert.Equal(inquiryId, invitation.InquiryId);
        Assert.Equal(ValuationMatchLevel.District, invitation.MatchLevel);
        Assert.Equal(UtcNow, invitation.SentAt);
    }

    [Fact]
    public void Create_WithEmptyAgencyId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationOfficeInvitation.Create(
            Guid.Empty, Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow));
    }

    [Fact]
    public void Create_WithEmptyInquiryId_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.Empty, ValuationMatchLevel.Neighborhood, UtcNow));
    }

    [Fact]
    public void Create_WithUndefinedMatchLevel_Throws()
    {
        Assert.Throws<DomainException>(() => ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), (ValuationMatchLevel)999, UtcNow));
    }

    [Theory]
    [InlineData(ValuationMatchLevel.Neighborhood)]
    [InlineData(ValuationMatchLevel.District)]
    [InlineData(ValuationMatchLevel.Governorate)]
    public void Create_WithEachMatchLevel_Succeeds(ValuationMatchLevel matchLevel)
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), matchLevel, UtcNow);

        Assert.Equal(matchLevel, invitation.MatchLevel);
    }

    [Fact]
    public void Create_StartsAsSent_WithNoResponseYet()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);

        Assert.Equal(ValuationOfficeInvitationStatus.Sent, invitation.Status);
        Assert.Null(invitation.RespondedAt);
    }

    [Fact]
    public void MarkResponded_FromSent_Succeeds_AndStampsRespondedAt()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);

        var respondedAt = UtcNow.AddHours(2);
        invitation.MarkResponded(respondedAt);

        Assert.Equal(ValuationOfficeInvitationStatus.Responded, invitation.Status);
        Assert.Equal(respondedAt, invitation.RespondedAt);
    }

    [Fact]
    public void MarkResponded_WhenAlreadyResponded_Throws()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);
        invitation.MarkResponded(UtcNow);

        Assert.Throws<DomainException>(() => invitation.MarkResponded(UtcNow));
    }

    [Fact]
    public void MarkResponded_WhenExpired_Throws()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);
        invitation.Expire(UtcNow);

        Assert.Throws<DomainException>(() => invitation.MarkResponded(UtcNow));
    }

    [Fact]
    public void Expire_FromSent_Succeeds()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);

        invitation.Expire(UtcNow.AddDays(1));

        Assert.Equal(ValuationOfficeInvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public void Expire_WhenAlreadyResponded_Throws()
    {
        // Status = Responded with RespondedAt != null must never regress to Expired.
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);
        invitation.MarkResponded(UtcNow);

        Assert.Throws<DomainException>(() => invitation.Expire(UtcNow));
    }

    [Fact]
    public void IsExpired_BeforeCutoff_IsFalse()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);

        Assert.False(invitation.IsExpired(UtcNow.AddHours(1), expiresAt: UtcNow.AddHours(24)));
    }

    [Fact]
    public void IsExpired_AfterCutoff_IsTrue()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);

        Assert.True(invitation.IsExpired(UtcNow.AddHours(25), expiresAt: UtcNow.AddHours(24)));
    }

    [Fact]
    public void IsExpired_OnceResponded_IsFalse()
    {
        var invitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), Guid.NewGuid(), ValuationMatchLevel.Neighborhood, UtcNow);
        invitation.MarkResponded(UtcNow);

        Assert.False(invitation.IsExpired(UtcNow.AddDays(1), expiresAt: UtcNow.AddHours(24)));
    }
}
