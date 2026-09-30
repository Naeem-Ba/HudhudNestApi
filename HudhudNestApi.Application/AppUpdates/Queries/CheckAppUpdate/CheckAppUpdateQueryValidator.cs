using FluentValidation;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;

namespace HudhudNestApi.Application.AppUpdates.Queries.CheckAppUpdate;

public sealed class CheckAppUpdateQueryValidator : AbstractValidator<CheckAppUpdateQuery>
{
    private static readonly string[] SupportedLanguages = ["ar", "en", "de"];

    public CheckAppUpdateQueryValidator()
    {
        RuleFor(x => x.Platform).IsInEnum();

        RuleFor(x => x.CurrentVersion)
            .NotEmpty()
            .Must(v => AppVersion.TryParse(v, out _))
            .WithMessage("CurrentVersion must be in the form major.minor.patch, e.g. 1.4.2.");

        RuleFor(x => x.Language)
            .Must(lang => lang is null || SupportedLanguages.Contains(lang))
            .WithMessage("Language must be one of ar, en, de.");
    }
}
