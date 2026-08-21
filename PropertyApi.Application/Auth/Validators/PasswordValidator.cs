using FluentValidation;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Validators;

/// <summary>
/// Async validator for password security requirements.
/// Validates complexity and checks against breach databases.
/// </summary>
public sealed class PasswordValidator : AbstractValidator<string>
{
    private readonly IPasswordSecurityService _passwordSecurityService;

    public PasswordValidator(IPasswordSecurityService passwordSecurityService)
    {
        _passwordSecurityService = passwordSecurityService;

        // Async validation rule
        RuleFor(x => x)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MustAsync(ValidatePasswordAsync)
            .WithMessage("Password validation failed.");
    }

    private async Task<bool> ValidatePasswordAsync(
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var result = await _passwordSecurityService.ValidatePasswordAsync(
            password,
            cancellationToken);

        // Store errors in the context for error message retrieval
        if (!result.IsValid)
        {
            // Collect all error messages
            var errorMessage = string.Join(" ", result.Errors);
            throw new ValidationException(errorMessage);
        }

        return result.IsValid;
    }
}

/// <summary>
/// Extension for registering password validation with custom error messages.
/// </summary>
public static class PasswordValidatorExtensions
{
    /// <summary>
    /// Adds async password security validation to a rule.
    /// </summary>
    public static IRuleBuilderOptions<T, string> MustHaveStrongPassword<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        IPasswordSecurityService passwordSecurityService)
    {
        return ruleBuilder
            .NotEmpty()
            .WithMessage("Password is required.")
            .MustAsync(async (password, ct) =>
            {
                var result = await passwordSecurityService.ValidatePasswordAsync(password, ct);
                return result.IsValid;
            })
            .DependentRules(() =>
            {
                ruleBuilder.SetValidator(new PasswordSecurityValidator(passwordSecurityService));
            });
    }
}

/// <summary>
/// Detailed password security validator that provides specific error messages.
/// </summary>
internal sealed class PasswordSecurityValidator : AbstractValidator<string>
{
    private readonly IPasswordSecurityService _passwordSecurityService;

    public PasswordSecurityValidator(IPasswordSecurityService passwordSecurityService)
    {
        _passwordSecurityService = passwordSecurityService;

        RuleFor(x => x)
            .MustAsync(ValidateAndDetailAsync);
    }

    private async Task<bool> ValidateAndDetailAsync(
        string password,
        CancellationToken cancellationToken)
    {
        var result = await _passwordSecurityService.ValidatePasswordAsync(password, cancellationToken);

        if (!result.IsValid && result.Errors.Any())
        {
            var errorMessage = string.Join(" ", result.Errors);
            throw new ValidationException(errorMessage);
        }

        return result.IsValid;
    }
}
