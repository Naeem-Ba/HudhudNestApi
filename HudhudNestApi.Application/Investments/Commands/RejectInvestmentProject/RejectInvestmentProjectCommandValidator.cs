using FluentValidation;

namespace HudhudNestApi.Application.Investments.Commands.RejectInvestmentProject;

public sealed class RejectInvestmentProjectCommandValidator : AbstractValidator<RejectInvestmentProjectCommand>
{
    public RejectInvestmentProjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("سبب الرفض مطلوب.")
            .MaximumLength(1000);
    }
}
