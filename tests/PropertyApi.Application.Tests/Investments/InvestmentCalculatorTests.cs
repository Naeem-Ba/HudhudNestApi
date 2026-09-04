using PropertyApi.Application.Investments.Services;

namespace PropertyApi.Application.Tests.Investments;

public sealed class InvestmentCalculatorTests
{
    [Fact]
    public void Calculate_TwelveMonthTerm_MatchesAnnualizedPercentDirectly()
    {
        var result = InvestmentCalculator.Calculate(
            Guid.NewGuid(), amount: 10_000m, termMonths: 12, currency: "USD",
            expectedReturnMinPercent: 5m, expectedReturnMaxPercent: 9m);

        Assert.Equal(500m, result.Conservative.EstimatedReturnAmount);
        Assert.Equal(10_500m, result.Conservative.EstimatedTotal);

        Assert.Equal(700m, result.Base.EstimatedReturnAmount); // (5+9)/2 = 7%
        Assert.Equal(900m, result.Optimistic.EstimatedReturnAmount);
    }

    [Fact]
    public void Calculate_SixMonthTerm_ProratesLinearly()
    {
        var result = InvestmentCalculator.Calculate(
            Guid.NewGuid(), amount: 10_000m, termMonths: 6, currency: "USD",
            expectedReturnMinPercent: 10m, expectedReturnMaxPercent: 10m);

        // 10% annualized over 6 months = 5% of principal.
        Assert.Equal(500m, result.Base.EstimatedReturnAmount);
    }

    [Fact]
    public void Calculate_AlwaysReturns_NonGuaranteedDisclaimer()
    {
        var result = InvestmentCalculator.Calculate(Guid.NewGuid(), 1000m, 12, "USD", 3m, 8m);

        Assert.False(string.IsNullOrWhiteSpace(result.Disclaimer));
        Assert.Contains("تقديري", result.Disclaimer);
    }

    [Fact]
    public void Calculate_ScenariosAreOrdered_ConservativeToOptimistic()
    {
        var result = InvestmentCalculator.Calculate(Guid.NewGuid(), 10_000m, 12, "USD", 4m, 12m);

        Assert.True(result.Conservative.EstimatedReturnAmount <= result.Base.EstimatedReturnAmount);
        Assert.True(result.Base.EstimatedReturnAmount <= result.Optimistic.EstimatedReturnAmount);
    }
}
