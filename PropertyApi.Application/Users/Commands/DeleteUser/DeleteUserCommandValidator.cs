using FluentValidation;

namespace PropertyApi.Application.Users.Commands.DeleteUser;

public sealed class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
        => RuleFor(x => x.UserId).NotEmpty();
}

