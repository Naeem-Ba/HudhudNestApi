using FluentValidation;

namespace HudhudNestApi.Application.Users.Messaging.Commands.MarkConversationRead;

public sealed class MarkConversationReadCommandValidator : AbstractValidator<MarkConversationReadCommand>
{
    public MarkConversationReadCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.OtherUserId).NotEmpty();
    }
}
