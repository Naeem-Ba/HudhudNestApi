using FluentValidation;

namespace HudhudNestApi.Application.Users.Messaging.Queries.GetConversations;

public sealed class GetConversationsQueryValidator : AbstractValidator<GetConversationsQuery>
{
    public GetConversationsQueryValidator()
    {
        RuleFor(x => x.PropertyId)
            .NotEqual(Guid.Empty)
            .When(x => x.PropertyId.HasValue);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50);
    }
}
