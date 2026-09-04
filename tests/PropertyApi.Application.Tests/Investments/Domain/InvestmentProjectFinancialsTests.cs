using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Tests.Investments.Domain;

public sealed class InvestmentProjectFinancialsTests
{
    [Fact]
    public void UpdateCosts_Recomputes_TotalProjectCost()
    {
        var financials = InvestmentProjectFinancials.Create(Guid.NewGuid());

        financials.UpdateCosts(
            purchasePrice: 100_000,
            renovationCost: 20_000,
            constructionCost: 0,
            taxes: 5_000,
            notaryCost: 1_000,
            brokerCost: 2_000,
            financingCost: 3_000,
            operatingCost: 4_000,
            contingencyReserve: 5_000);

        Assert.Equal(140_000m, financials.TotalProjectCost);
    }

    [Fact]
    public void UpdateCosts_Rejects_NegativeValues()
    {
        var financials = InvestmentProjectFinancials.Create(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            financials.UpdateCosts(-1, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void UpdateRevenueProjections_Rejects_NegativeValues()
    {
        var financials = InvestmentProjectFinancials.Create(Guid.NewGuid());

        Assert.Throws<DomainException>(() => financials.UpdateRevenueProjections(-1, 100));
        Assert.Throws<DomainException>(() => financials.UpdateRevenueProjections(100, -1));
    }
}
