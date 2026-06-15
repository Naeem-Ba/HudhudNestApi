using FluentValidation;
using PropertyApi.Application.Properties.Validators;

namespace PropertyApi.Application.Listings.Queries.GetPropertiesList;

public sealed class GetPropertiesListQueryValidator
    : AbstractValidator<GetPropertiesListQuery>
{
    public GetPropertiesListQueryValidator()
    {
        RuleFor(query => query.Filter)
            .NotNull()
            .SetValidator(new PropertyFilterDtoValidator());
    }
}
