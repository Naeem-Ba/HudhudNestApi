using PropertyApi.Application.Investments.DTOs;

namespace PropertyApi.Application.Investments.Services;

/// <summary>
/// Pure, stateless, indicative-only return calculator (Phase 1 spec §21). Never persists
/// anything, never creates an investment, and is not wired to any payment concept — it only
/// turns a project's ExpectedReturnMin/Max range into three illustrative scenarios.
/// </summary>
public static class InvestmentCalculator
{
    public const string Disclaimer =
        "العائد المتوقع تقديري وليس مضمونًا، وقد يتعرض رأس المال لخسارة جزئية أو كاملة حسب طبيعة المشروع.";

    public static InvestmentCalculatorResultDto Calculate(
        Guid investmentProjectId,
        decimal amount,
        int termMonths,
        string currency,
        decimal expectedReturnMinPercent,
        decimal expectedReturnMaxPercent)
    {
        var basePercent = (expectedReturnMinPercent + expectedReturnMaxPercent) / 2m;

        return new InvestmentCalculatorResultDto(
            investmentProjectId,
            amount,
            termMonths,
            currency,
            BuildScenario(amount, termMonths, expectedReturnMinPercent),
            BuildScenario(amount, termMonths, basePercent),
            BuildScenario(amount, termMonths, expectedReturnMaxPercent),
            Disclaimer);
    }

    private static InvestmentCalculatorScenarioDto BuildScenario(
        decimal amount,
        int termMonths,
        decimal annualizedReturnPercent)
    {
        // ExpectedReturnMin/Max are stored as annualized percentages; the term is prorated
        // linearly across the months the money is committed for. Indicative only — see
        // Disclaimer.
        var returnAmount = Math.Round(
            amount * (annualizedReturnPercent / 100m) * (termMonths / 12m),
            2,
            MidpointRounding.AwayFromZero);

        return new InvestmentCalculatorScenarioDto(
            InitialAmount: amount,
            EstimatedReturnAmount: returnAmount,
            EstimatedTotal: amount + returnAmount,
            AnnualizedReturnPercent: annualizedReturnPercent);
    }
}
