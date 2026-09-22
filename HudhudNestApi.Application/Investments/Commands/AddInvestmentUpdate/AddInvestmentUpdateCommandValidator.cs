using FluentValidation;
using HudhudNestApi.Application.Investments.Validation;

namespace HudhudNestApi.Application.Investments.Commands.AddInvestmentUpdate;

public sealed class AddInvestmentUpdateCommandValidator : AbstractValidator<AddInvestmentUpdateCommand>
{
    public AddInvestmentUpdateCommandValidator()
    {
        RuleFor(x => x.InvestmentProjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(8000).MustNotImplyGuaranteedReturn();
        RuleFor(x => x.UpdateType).IsInEnum();
    }
}
