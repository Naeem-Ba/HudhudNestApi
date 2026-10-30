using FluentValidation;
using HudhudNestApi.Application.Common.Validation;

namespace HudhudNestApi.Application.Investments.Queries.GetAdminInvestmentProjects;

public sealed class GetAdminInvestmentProjectsQueryValidator : AbstractValidator<GetAdminInvestmentProjectsQuery>
{
    public GetAdminInvestmentProjectsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThan(0);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Filter.Search).MaximumLength(100).NoNulCharacters();
    }
}
