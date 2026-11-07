using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Queries.GetMyAgency;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Agencies;

/// <summary>
/// GET /api/agencies/me carries the owner's plan state, so the client can keep the office visible (with a
/// renewal notice) for the grace period after the owner's plan ends - for the owner and for every member.
/// </summary>
public sealed class GetMyAgencyOwnerPlanTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static (GetMyAgencyQueryHandler Handler, Guid MemberId, UserAccount Owner) Build(
        Action<UserAccount> configureOwner)
    {
        var owner = UserAccount.Create(Guid.NewGuid(), "Owner", "One", Now.AddDays(-60));
        configureOwner(owner);

        var agency = Agency.Create("Office", "office", owner.Id, "SY", Now.AddDays(-60));
        owner.JoinAgency(agency.Id, Now.AddDays(-60));

        var member = UserAccount.Create(Guid.NewGuid(), "Member", "Two", Now.AddDays(-30));
        member.JoinAgency(agency.Id, Now.AddDays(-30));

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetUserAccountAsync(member.Id, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        agencies.Setup(x => x.GetMembersAsync(agency.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccount> { owner, member });

        return (new GetMyAgencyQueryHandler(agencies.Object), member.Id, owner);
    }

    [Fact]
    public async Task AMember_SeesTheOwnersExpiredPlan_AndItsEndDate()
    {
        var (handler, memberId, owner) = Build(o =>
            o.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(-50), Guid.NewGuid(), Now.AddDays(-60)));

        var dto = await handler.Handle(new GetMyAgencyQuery(memberId), CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("Expired", dto!.OwnerPlanStatus);
        Assert.Equal(owner.PlanExpiresAt, dto.OwnerPlanExpiresAt);
        Assert.Equal(DateTimeKind.Utc, dto.OwnerPlanExpiresAt!.Value.Kind);
    }

    [Fact]
    public async Task AnOwnerWithoutAPlan_IsReportedAsNoPlan_WithNoEndDate()
    {
        var (handler, memberId, _) = Build(_ => { });

        var dto = await handler.Handle(new GetMyAgencyQuery(memberId), CancellationToken.None);

        Assert.Equal("NoPlan", dto!.OwnerPlanStatus);
        Assert.Null(dto.OwnerPlanExpiresAt);
    }

    [Fact]
    public async Task AnOwnerWithAPlanInForce_IsReportedAsActive()
    {
        var (handler, memberId, _) = Build(o =>
            o.ActivatePlanByAdmin(Guid.NewGuid(), Now.AddDays(20), Guid.NewGuid(), Now.AddDays(-10)));

        var dto = await handler.Handle(new GetMyAgencyQuery(memberId), CancellationToken.None);

        Assert.Equal("Active", dto!.OwnerPlanStatus);
    }
}
