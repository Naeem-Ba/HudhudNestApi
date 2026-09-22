using FluentValidation;

namespace HudhudNestApi.Application.AppUpdates.Queries.GetAdminAppReleases;

public sealed class GetAdminAppReleasesQueryValidator : AbstractValidator<GetAdminAppReleasesQuery>
{
    public GetAdminAppReleasesQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThan(0);
        RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
    }
}
