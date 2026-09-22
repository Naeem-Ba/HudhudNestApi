using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Domain.Investments.Entities;

/// <summary>
/// Indicative risk breakdown for one <see cref="InvestmentProject"/> (one-to-one). Phase 1 has no
/// financial risk engine (spec §8) — this is a structured, staff-entered assessment, never a
/// derived/guaranteed figure. RiskSummary must never claim a guaranteed or risk-free return;
/// enforced by <c>InvestmentRiskAssessmentValidators</c> in the Application layer.
/// </summary>
public sealed class InvestmentRiskAssessment : BaseEntity
{
    public Guid InvestmentProjectId { get; private set; }

    public InvestmentRiskLevel RiskLevel { get; private set; }
    public InvestmentRiskLevel MarketRisk { get; private set; }
    public InvestmentRiskLevel LiquidityRisk { get; private set; }
    public InvestmentRiskLevel ProjectRisk { get; private set; }
    public InvestmentRiskLevel FinancingRisk { get; private set; }
    public InvestmentRiskLevel DeveloperRisk { get; private set; }

    /// <summary>0-100 indicative score — higher means riskier. Staff-entered, not derived.</summary>
    public int RiskScore { get; private set; }

    public string RiskSummary { get; private set; } = string.Empty;

    private InvestmentRiskAssessment() { }

    public static InvestmentRiskAssessment Create(
        Guid investmentProjectId,
        InvestmentRiskLevel riskLevel,
        InvestmentRiskLevel marketRisk,
        InvestmentRiskLevel liquidityRisk,
        InvestmentRiskLevel projectRisk,
        InvestmentRiskLevel financingRisk,
        InvestmentRiskLevel developerRisk,
        int riskScore,
        string riskSummary)
    {
        if (investmentProjectId == Guid.Empty)
            throw new DomainException("مشروع الاستثمار مطلوب.");

        var assessment = new InvestmentRiskAssessment { InvestmentProjectId = investmentProjectId };
        assessment.Update(riskLevel, marketRisk, liquidityRisk, projectRisk, financingRisk, developerRisk, riskScore, riskSummary);
        return assessment;
    }

    public void Update(
        InvestmentRiskLevel riskLevel,
        InvestmentRiskLevel marketRisk,
        InvestmentRiskLevel liquidityRisk,
        InvestmentRiskLevel projectRisk,
        InvestmentRiskLevel financingRisk,
        InvestmentRiskLevel developerRisk,
        int riskScore,
        string riskSummary)
    {
        if (riskScore is < 0 or > 100)
            throw new DomainException("درجة المخاطر يجب أن تكون بين 0 و 100.");
        if (string.IsNullOrWhiteSpace(riskSummary))
            throw new DomainException("ملخص المخاطر مطلوب.");

        RiskLevel = riskLevel;
        MarketRisk = marketRisk;
        LiquidityRisk = liquidityRisk;
        ProjectRisk = projectRisk;
        FinancingRisk = financingRisk;
        DeveloperRisk = developerRisk;
        RiskScore = riskScore;
        RiskSummary = riskSummary.Trim();
    }
}
