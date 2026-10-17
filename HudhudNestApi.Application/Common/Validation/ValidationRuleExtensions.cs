using FluentValidation;

namespace HudhudNestApi.Application.Common.Validation;

/// <summary>
/// Rules shared by the validators that accept free text and money from the public API.
/// Both exist because PostgreSQL rejects the value after the validator has accepted it, which
/// surfaced as an opaque 500 (QA Phase 8, P8-D01).
/// </summary>
public static class ValidationRuleExtensions
{
    /// <summary>
    /// The largest money amount a listing may carry, in the listing's own currency. The columns
    /// are numeric(18,4), so anything from 1e14 up fails at the database; 1e12 stays well inside
    /// that and is still far above any real price (a billion-dollar tower in Syrian pounds
    /// included).
    /// </summary>
    public const decimal MaxMoneyAmount = 1_000_000_000_000m;

    /// <summary>PostgreSQL text cannot hold the NUL character; sending one makes the insert fail.</summary>
    public static IRuleBuilderOptions<T, string?> NoNulCharacters<T>(this IRuleBuilder<T, string?> rule)
        => rule
            .Must(value => value is null || !value.Contains('\0'))
            .WithMessage("{PropertyName} contains an invalid character.")
            .WithErrorCode("INVALID_CHARACTER");

    /// <summary>Area is numeric(10,2): 1e8 and above fails at the database.</summary>
    public const decimal MaxArea = 100_000_000m;

    public static IRuleBuilderOptions<T, decimal?> WithinAreaRange<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(value => value is null || value < MaxArea)
            .WithMessage("{PropertyName} is larger than the allowed maximum.")
            .WithErrorCode("AMOUNT_TOO_LARGE");

    public static IRuleBuilderOptions<T, decimal> WithinMoneyRange<T>(this IRuleBuilder<T, decimal> rule)
        => rule
            .LessThan(MaxMoneyAmount)
            .WithMessage("{PropertyName} is larger than the allowed maximum.")
            .WithErrorCode("AMOUNT_TOO_LARGE");

    public static IRuleBuilderOptions<T, decimal?> WithinMoneyRange<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(value => value is null || value < MaxMoneyAmount)
            .WithMessage("{PropertyName} is larger than the allowed maximum.")
            .WithErrorCode("AMOUNT_TOO_LARGE");
}
