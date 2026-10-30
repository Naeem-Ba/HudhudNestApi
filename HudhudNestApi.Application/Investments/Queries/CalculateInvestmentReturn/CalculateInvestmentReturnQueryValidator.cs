using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.Investments.Queries.CalculateInvestmentReturn;

public sealed class CalculateInvestmentReturnQueryValidator : AbstractValidator<CalculateInvestmentReturnQuery>
{
    /// <summary>Fifty years: longer than any project term the admin form accepts.</summary>
    public const int MaxTermMonths = 600;

    public CalculateInvestmentReturnQueryValidator()
    {
        RuleFor(x => x.InvestmentProjectId).NotEmpty();
        // QA Phase 10, P10-D07: the endpoint is anonymous; an unbounded amount overflowed System.Decimal (500) and an
        // unbounded term produced absurd "estimates". Same money ceiling as every other amount in the API.
        RuleFor(x => x.Amount).GreaterThan(0).WithinMoneyRange();
        RuleFor(x => x.TermMonths).GreaterThan(0).LessThanOrEqualTo(MaxTermMonths).When(x => x.TermMonths.HasValue);
    }
}
