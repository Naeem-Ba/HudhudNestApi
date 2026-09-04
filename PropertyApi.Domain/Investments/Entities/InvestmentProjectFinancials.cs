using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.Investments.Entities;

/// <summary>
/// Detailed cost/revenue breakdown for one <see cref="InvestmentProject"/>. Kept as its own
/// aggregate (one-to-one with the project) rather than a pile of decimals on InvestmentProject
/// itself, per Phase 1 spec §6. All amounts are <c>decimal</c> — never <c>double</c>.
/// </summary>
public sealed class InvestmentProjectFinancials : BaseEntity
{
    public Guid InvestmentProjectId { get; private set; }

    public decimal PurchasePrice { get; private set; }
    public decimal RenovationCost { get; private set; }
    public decimal ConstructionCost { get; private set; }
    public decimal Taxes { get; private set; }
    public decimal NotaryCost { get; private set; }
    public decimal BrokerCost { get; private set; }
    public decimal FinancingCost { get; private set; }
    public decimal OperatingCost { get; private set; }
    public decimal ContingencyReserve { get; private set; }
    public decimal ExpectedRevenue { get; private set; }
    public decimal ExpectedProfit { get; private set; }

    /// <summary>Sum of every cost component above. Recomputed whenever a cost changes — stored
    /// rather than a pure computed property so it can be selected/sorted on directly.</summary>
    public decimal TotalProjectCost { get; private set; }

    private InvestmentProjectFinancials() { }

    public static InvestmentProjectFinancials Create(Guid investmentProjectId)
    {
        if (investmentProjectId == Guid.Empty)
            throw new DomainException("مشروع الاستثمار مطلوب.");

        return new InvestmentProjectFinancials
        {
            InvestmentProjectId = investmentProjectId,
        };
    }

    public void UpdateCosts(
        decimal purchasePrice,
        decimal renovationCost,
        decimal constructionCost,
        decimal taxes,
        decimal notaryCost,
        decimal brokerCost,
        decimal financingCost,
        decimal operatingCost,
        decimal contingencyReserve)
    {
        foreach (var value in new[]
                 {
                     purchasePrice, renovationCost, constructionCost, taxes, notaryCost,
                     brokerCost, financingCost, operatingCost, contingencyReserve,
                 })
        {
            if (value < 0)
                throw new DomainException("قيم التكاليف لا يمكن أن تكون سالبة.");
        }

        PurchasePrice = purchasePrice;
        RenovationCost = renovationCost;
        ConstructionCost = constructionCost;
        Taxes = taxes;
        NotaryCost = notaryCost;
        BrokerCost = brokerCost;
        FinancingCost = financingCost;
        OperatingCost = operatingCost;
        ContingencyReserve = contingencyReserve;

        RecomputeTotalProjectCost();
    }

    public void UpdateRevenueProjections(decimal expectedRevenue, decimal expectedProfit)
    {
        if (expectedRevenue < 0)
            throw new DomainException("الإيراد المتوقع لا يمكن أن يكون سالبًا.");
        if (expectedProfit < 0)
            throw new DomainException("الربح المتوقع لا يمكن أن يكون سالبًا.");

        ExpectedRevenue = expectedRevenue;
        ExpectedProfit = expectedProfit;
    }

    private void RecomputeTotalProjectCost()
    {
        TotalProjectCost = PurchasePrice + RenovationCost + ConstructionCost + Taxes +
                            NotaryCost + BrokerCost + FinancingCost + OperatingCost +
                            ContingencyReserve;
    }
}
