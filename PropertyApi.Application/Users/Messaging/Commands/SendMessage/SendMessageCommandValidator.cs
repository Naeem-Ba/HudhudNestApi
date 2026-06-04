using FluentValidation;

namespace PropertyApi.Application.Users.Messaging.Commands.SendMessage;

public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.PropertyId)
            .NotEmpty();

        RuleFor(x => x.ReceiverId)
            .NotEqual(Guid.Empty)
            .When(x => x.ReceiverId.HasValue);

        RuleFor(x => x.Content)
            .NotEmpty()
            .MaximumLength(3000);
    }
}
