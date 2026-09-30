using FluentValidation;
using HudhudNestApi.Application.Properties.Validators;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertiesList;

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
