using FluentValidation;

namespace PropertyApi.Application.Investments.Queries.GetAdminInvestmentProjects;

public sealed class GetAdminInvestmentProjectsQueryValidator : AbstractValidator<GetAdminInvestmentProjectsQuery>
{
    public GetAdminInvestmentProjectsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThan(0);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
    }
}
