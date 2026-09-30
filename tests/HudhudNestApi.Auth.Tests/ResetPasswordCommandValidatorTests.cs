using FluentValidation.TestHelper;
using Xunit;
using Moq;
using HudhudNestApi.Application.Auth.Commands.ResetPassword;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Auth.Tests;

/// <summary>
/// Tests that ResetPasswordCommandValidator applies the same password-security policy
/// (complexity + breach screening) as RegisterCommandValidator, via IPasswordSecurityService.
/// </summary>
public sealed class ResetPasswordCommandValidatorTests
{
    private readonly Mock<IPasswordSecurityService> _passwordSecurityServiceMock;
    private readonly ResetPasswordCommandValidator _validator;

    public ResetPasswordCommandValidatorTests()
    {
        _passwordSecurityServiceMock = new Mock<IPasswordSecurityService>();

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasswordValidationResult.Success());

        _validator = new ResetPasswordCommandValidator(_passwordSecurityServiceMock.Object);
    }

    [Fact]
    public async Task Validate_WithBreachedPassword_HasSecurityError()
    {
        var command = new ResetPasswordCommand(
            Email: "user@example.com",
            Token: "valid-token",
            NewPassword: "Password123!",
            ConfirmPassword: "Password123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("Password123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure(new PasswordValidationError(
                PasswordErrorCodes.Breached,
                "This password has been exposed in known data breaches. " +
                "Please choose a different password.")));

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public async Task Validate_WithBreachedPassword_CarriesTheStableErrorCodeOntoTheFailure()
    {
        var command = new ResetPasswordCommand(
            Email: "user@example.com",
            Token: "valid-token",
            NewPassword: "Password123!",
            ConfirmPassword: "Password123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("Password123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure(new PasswordValidationError(
                PasswordErrorCodes.Breached,
                "This password has been exposed in known data breaches. " +
                "Please choose a different password.")));

        var result = await _validator.TestValidateAsync(command);

        var failure = Assert.Single(
            result.Errors,
            error => error.PropertyName == nameof(ResetPasswordCommand.NewPassword));

        Assert.Equal(PasswordErrorCodes.Breached, failure.ErrorCode);
    }

    [Fact]
    public async Task Validate_WithComplexPasswordButNoUppercase_HasSecurityError()
    {
        var command = new ResetPasswordCommand(
            Email: "user@example.com",
            Token: "valid-token",
            NewPassword: "password123!",
            ConfirmPassword: "password123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("password123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure(new PasswordValidationError(
                PasswordErrorCodes.NoUppercase,
                "Password must contain at least one uppercase letter (A-Z).")));

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public async Task Validate_WithValidCompleteCommand_HasNoErrors()
    {
        var command = new ResetPasswordCommand(
            Email: "user@example.com",
            Token: "valid-token",
            NewPassword: "SecurePass123!",
            ConfirmPassword: "SecurePass123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("SecurePass123!", default))
            .ReturnsAsync(PasswordValidationResult.Success());

        var result = await _validator.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Validate_WithMismatchedConfirmPassword_HasError()
    {
        var command = new ResetPasswordCommand(
            Email: "user@example.com",
            Token: "valid-token",
            NewPassword: "SecurePass123!",
            ConfirmPassword: "Different123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("SecurePass123!", default))
            .ReturnsAsync(PasswordValidationResult.Success());

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }
}
