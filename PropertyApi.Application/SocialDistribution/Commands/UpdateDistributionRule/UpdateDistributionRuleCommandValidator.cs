using FluentValidation;

namespace PropertyApi.Application.SocialDistribution.Commands.UpdateDistributionRule;

public sealed class UpdateDistributionRuleCommandValidator : AbstractValidator<UpdateDistributionRuleCommand>
{
    public UpdateDistributionRuleCommandValidator()
    {
        RuleFor(x => x.RuleId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ProvinceId).GreaterThan(0).When(x => x.ProvinceId is not null);
        RuleFor(x => x.PropertyTypeId).GreaterThan(0).When(x => x.PropertyTypeId is not null);
        RuleFor(x => x.TransactionType).IsInEnum().When(x => x.TransactionType is not null);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).When(x => x.StartAt is not null && x.EndAt is not null);
    }
}
