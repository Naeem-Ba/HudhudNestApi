using FluentValidation.TestHelper;
using Xunit;
using Moq;
using PropertyApi.Application.Auth.Commands.Register;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Auth.Tests;

/// <summary>
/// Tests for RegisterCommandValidator with enhanced password security validation.
/// Ensures the validator properly integrates password security checks.
/// </summary>
public sealed class RegisterCommandValidatorTests
{
    private readonly Mock<IPasswordSecurityService> _passwordSecurityServiceMock;
    private readonly RegisterCommandValidator _validator;

    public RegisterCommandValidatorTests()
    {
        _passwordSecurityServiceMock = new Mock<IPasswordSecurityService>();

        // Default: any password the test does not care about passes security screening.
        //
        // Without this, Moq returns a completed Task whose Result is null for every
        // un-stubbed call, so the validator's password rule threw NullReferenceException
        // and every FirstName/LastName/Email test failed for a reason that had nothing
        // to do with what it was asserting. Tests that exercise password screening
        // override this with their own Setup for their specific password.
        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasswordValidationResult.Success());

        _validator = new RegisterCommandValidator(_passwordSecurityServiceMock.Object);
    }

    #region First Name Tests

    [Fact]
    public async Task Validate_WithEmptyFirstName_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: string.Empty,
            LastName: "Doe",
            Email: "john@example.com",
            Password: "ValidPass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public async Task Validate_WithFirstNameExceedingMaxLength_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: new string('a', 101),
            LastName: "Doe",
            Email: "john@example.com",
            Password: "ValidPass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    #endregion

    #region Last Name Tests

    [Fact]
    public async Task Validate_WithEmptyLastName_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: string.Empty,
            Email: "john@example.com",
            Password: "ValidPass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    #endregion

    #region Email Tests

    [Fact]
    public async Task Validate_WithEmptyEmail_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: string.Empty,
            Password: "ValidPass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public async Task Validate_WithInvalidEmailFormat_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "not-an-email",
            Password: "ValidPass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public async Task Validate_WithEmailContainingWhitespace_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john @example.com",
            Password: "ValidPass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Email must not contain whitespace.");
    }

    #endregion

    #region Password - Basic Validation Tests

    [Fact]
    public async Task Validate_WithEmptyPassword_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: string.Empty);

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public async Task Validate_WithPasswordLessThan8Characters_HasError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "Pass1!");

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must be at least 8 characters long.");
    }

    #endregion

    #region Password - Security Validation Tests

    [Fact]
    public async Task Validate_WithComplexPasswordButNoUppercase_HasSecurityError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "password123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("password123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure("Password must contain at least one uppercase letter (A-Z)."));

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public async Task Validate_WithValidComplexPassword_HasNoError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "ValidPass1!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("ValidPass1!", default))
            .ReturnsAsync(PasswordValidationResult.Success());

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public async Task Validate_WithBreachedPassword_HasSecurityError()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "Password123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("Password123!", default))
            .ReturnsAsync(PasswordValidationResult.Failure(
                "This password has been exposed in known data breaches. Please choose a different password."));

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public async Task Validate_WithMultiplePasswordErrors_ReturnsAllErrors()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john@example.com",
            Password: "weak");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("weak", default))
            .ReturnsAsync(PasswordValidationResult.Failure(
                "Password must be at least 8 characters long.",
                "Password must contain at least one uppercase letter (A-Z).",
                "Password must contain at least one digit (0-9).",
                "Password must contain at least one special character: !@#$%^&*()_+-=[]{}|;:',.<>?/~`"));

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Validate_WithValidCompleteCommand_HasNoErrors()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "john.doe@example.com",
            Password: "SecurePass123!");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("SecurePass123!", default))
            .ReturnsAsync(PasswordValidationResult.Success());

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Validate_WithInvalidEmail_And_WeakPassword_ReturnsMultipleErrors()
    {
        // Arrange
        var command = new RegisterCommand(
            FirstName: "John",
            LastName: "Doe",
            Email: "invalid-email",
            Password: "weak");

        _passwordSecurityServiceMock
            .Setup(x => x.ValidatePasswordAsync("weak", default))
            .ReturnsAsync(PasswordValidationResult.Failure(
                "Password must be at least 8 characters long.",
                "Password must contain at least one uppercase letter (A-Z)."));

        // Act
        var result = await _validator.TestValidateAsync(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    #endregion
}
