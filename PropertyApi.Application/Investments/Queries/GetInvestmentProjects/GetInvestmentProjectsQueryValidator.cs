using FluentValidation;

namespace PropertyApi.Application.Investments.Queries.GetInvestmentProjects;

public sealed class GetInvestmentProjectsQueryValidator : AbstractValidator<GetInvestmentProjectsQuery>
{
    public GetInvestmentProjectsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThan(0);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
    }
}
