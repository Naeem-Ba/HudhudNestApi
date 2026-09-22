using FluentValidation;
using FluentValidation.Results;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Application.Users.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    private readonly IPasswordSecurityService _passwordSecurityService;

    public ChangePasswordCommandValidator(
        IPasswordSecurityService passwordSecurityService)
    {
        _passwordSecurityService = passwordSecurityService;

        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();

        // Was: MinimumLength(8) + ad-hoc uppercase/digit regexes only — weaker than, and
        // inconsistent with, Register/Reset (no lowercase/special-character check, no
        // breach screening). Delegating to the same abstraction those two use closes that
        // gap instead of maintaining a third, slightly different copy of the same policy.
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters long.")
            .CustomAsync(ValidatePasswordSecurityAsync);
    }

    private async Task ValidatePasswordSecurityAsync(
        string password,
        ValidationContext<ChangePasswordCommand> context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var result = await _passwordSecurityService.ValidatePasswordAsync(
            password,
            cancellationToken);

        if (result.IsValid)
        {
            return;
        }

        foreach (var error in result.Errors)
        {
            context.AddFailure(new ValidationFailure(
                nameof(ChangePasswordCommand.NewPassword),
                error.Message)
            {
                ErrorCode = error.Code
            });
        }
    }
}

