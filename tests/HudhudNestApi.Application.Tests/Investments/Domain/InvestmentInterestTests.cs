using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Tests.Investments.Domain;

public sealed class InvestmentInterestTests
{
    [Fact]
    public void Create_StartsActive()
    {
        var interest = InvestmentInterest.Create(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(InvestmentInterestStatus.Active, interest.Status);
    }

    [Fact]
    public void Withdraw_ThenReactivate_RoundTrips()
    {
        var interest = InvestmentInterest.Create(Guid.NewGuid(), Guid.NewGuid());

        interest.Withdraw();
        Assert.Equal(InvestmentInterestStatus.Withdrawn, interest.Status);

        interest.Reactivate();
        Assert.Equal(InvestmentInterestStatus.Active, interest.Status);
    }

    [Fact]
    public void Withdraw_Twice_Throws()
    {
        var interest = InvestmentInterest.Create(Guid.NewGuid(), Guid.NewGuid());
        interest.Withdraw();

        Assert.Throws<DomainException>(() => interest.Withdraw());
    }

    [Fact]
    public void Reactivate_WhenAlreadyActive_Throws()
    {
        var interest = InvestmentInterest.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<DomainException>(() => interest.Reactivate());
    }
}
