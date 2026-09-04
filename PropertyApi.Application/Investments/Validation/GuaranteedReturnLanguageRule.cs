using FluentValidation;

namespace PropertyApi.Application.Investments.Validation;

/// <summary>
/// Shared FluentValidation rule rejecting language that implies a guaranteed return or a
/// risk-free investment — used on every free-text field an admin can publish (project
/// description, risk summary, update content). Phase 1 spec §8/§39: never imply a guaranteed
/// return or protected capital.
/// </summary>
public static class GuaranteedReturnLanguageRule
{
    private static readonly string[] BannedPhrases =
    {
        "guaranteed return", "guaranteed profit", "risk free", "risk-free", "zero risk",
        "no risk", "100% safe", "capital protected", "guaranteed income",
        "عائد مضمون", "ربح مضمون", "استثمار مضمون", "بدون مخاطر", "خالي من المخاطر",
        "بدون أي مخاطر", "مضمون 100", "استثمار آمن تماما", "بلا مخاطر",
    };

    public static IRuleBuilderOptions<T, string?> MustNotImplyGuaranteedReturn<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder.Must(text =>
                string.IsNullOrWhiteSpace(text) ||
                !BannedPhrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("لا يمكن استخدام لغة توحي بضمان العائد أو انعدام المخاطر.");
    }
}
