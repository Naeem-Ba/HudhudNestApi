using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.Investments.Queries.GetInvestmentProjects;

public sealed class GetInvestmentProjectsQueryValidator : AbstractValidator<GetInvestmentProjectsQuery>
{
    public GetInvestmentProjectsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThan(0);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
        // QA Phase 10, P10-D06: a NUL in the search text reached PostgreSQL and answered 500 for an anonymous caller.
        RuleFor(x => x.Filter.Search).MaximumLength(100).NoNulCharacters();
        RuleFor(x => x.Filter.City).MaximumLength(100).NoNulCharacters();
    }
}
