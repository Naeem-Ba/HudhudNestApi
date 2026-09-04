namespace PropertyApi.Domain.Investments.Enums;

/// <summary>
/// Indicative risk classification — never rendered as, or implying, a guarantee.
/// See Phase 1 spec §8/§39: no "risk free" or "guaranteed" language anywhere this is displayed.
/// </summary>
public enum InvestmentRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    VeryHigh = 3,
}
