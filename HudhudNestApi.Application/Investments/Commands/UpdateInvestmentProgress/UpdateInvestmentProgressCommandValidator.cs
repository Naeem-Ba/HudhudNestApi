using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.Investments.Commands.UpdateInvestmentProgress;

public sealed class UpdateInvestmentProgressCommandValidator : AbstractValidator<UpdateInvestmentProgressCommand>
{
    public UpdateInvestmentProgressCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RaisedAmount).GreaterThanOrEqualTo(0).WithinMoneyRange();
    }
}
