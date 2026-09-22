using FluentValidation.TestHelper;
using Moq;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Users.Commands.ChangePassword;
using Xunit;

namespace HudhudNestApi.Application.Tests.Users;

/// <summary>
/// Tests that ChangePasswordCommandValidator applies the same password-security policy
/// (complexity + breach screening) as Register/Reset, via IPasswordSecurityService.
/// </summary>
public sealed class ChangePasswordCommandValidatorTests
{
    private readonly Mock<IPasswordSecurityService> _passwordSecurityServiceMock;
    private readonly ChangePasswordCommandValidator _validator;

    public ChangePasswordCommandValidatorTests()
    {
        _passwordSecurityServiceMock = new Mock<IPasswordSecurityService>();

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasswordValidationResult.Success());

        _validator = new ChangePasswordCommandValidator(_passwordSecurityServiceMock.Object);
    }

    [Fact]
    public async Task Validate_WithBreachedPassword_HasSecurityError()
    {
        var command = new ChangePasswordCommand(
            UserId: Guid.NewGuid(),
            CurrentPassword: "OldPass123!",
            NewPassword: "Password123!");

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
        var command = new ChangePasswordCommand(
            UserId: Guid.NewGuid(),
            CurrentPassword: "OldPass123!",
            NewPassword: "Password123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("Password123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure(new PasswordValidationError(
                PasswordErrorCodes.Breached,
                "This password has been exposed in known data breaches. " +
                "Please choose a different password.")));

        var result = await _validator.TestValidateAsync(command);

        var failure = Assert.Single(
            result.Errors,
            error => error.PropertyName == nameof(ChangePasswordCommand.NewPassword));

        Assert.Equal(PasswordErrorCodes.Breached, failure.ErrorCode);
    }

    [Fact]
    public async Task Validate_WithNoLowercase_HasSecurityError()
    {
        // Regression guard: the old regex-only rule only checked uppercase/digit, so a
        // password with no lowercase letter at all previously passed this validator.
        var command = new ChangePasswordCommand(
            UserId: Guid.NewGuid(),
            CurrentPassword: "OldPass123!",
            NewPassword: "PASSWORD123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("PASSWORD123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure(new PasswordValidationError(
                PasswordErrorCodes.NoLowercase,
                "Password must contain at least one lowercase letter (a-z).")));

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public async Task Validate_WithValidCompleteCommand_HasNoErrors()
    {
        var command = new ChangePasswordCommand(
            UserId: Guid.NewGuid(),
            CurrentPassword: "OldPass123!",
            NewPassword: "SecurePass123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("SecurePass123!", default))
            .ReturnsAsync(PasswordValidationResult.Success());

        var result = await _validator.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Validate_WithEmptyUserId_HasError()
    {
        var command = new ChangePasswordCommand(
            UserId: Guid.Empty,
            CurrentPassword: "OldPass123!",
            NewPassword: "SecurePass123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("SecurePass123!", default))
            .ReturnsAsync(PasswordValidationResult.Success());

        var result = await _validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }
}
