using FluentValidation;

namespace HudhudNestApi.Application.Users.Messaging.Queries.GetMessages;

public sealed class GetMessagesQueryValidator : AbstractValidator<GetMessagesQuery>
{
    public GetMessagesQueryValidator()
    {
        RuleFor(x => x.PropertyId)
            .NotEmpty();

        RuleFor(x => x.OtherUserId)
            .NotEqual(Guid.Empty)
            .When(x => x.OtherUserId.HasValue);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50);
    }
}

