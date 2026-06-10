using FluentValidation.TestHelper;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Auth.Tests.Application.Validators;

[Trait("Category", "Validators")]
[Trait("Validator", "SendPhoneOtp")]
public sealed class SendPhoneOtpCommandValidatorTests
{
    private readonly SendPhoneOtpCommandValidator _validator = new();

    [Theory(DisplayName = "Valid international phone number passes")]
    [InlineData("+963911234567")]
    [InlineData("+4915112345678")]
    [InlineData("+971501234567")]
    [InlineData("+12025550123")]
    [InlineData("+447911123456")]
    public void ValidPhoneNumber_ShouldPass(string phone)
    {
        var result = _validator.TestValidate(new SendPhoneOtpCommand(phone));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory(DisplayName = "Invalid phone number fails")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("963911234567")]
    [InlineData("00963911234567")]
    [InlineData("+123")]
    [InlineData("+12345678901234567")]
    [InlineData("+963 911 234 567")]
    [InlineData("+963-911-234")]
    [InlineData("+96391123ABCD")]
    [InlineData("+0911234567")]
    public void InvalidPhoneNumber_ShouldFail(string phone)
    {
        var result = _validator.TestValidate(new SendPhoneOtpCommand(phone));
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }
}

[Trait("Category", "Validators")]
[Trait("Validator", "VerifyPhoneOtp")]
public sealed class VerifyPhoneOtpCommandValidatorTests
{
    private readonly VerifyPhoneOtpCommandValidator _validator = new();

    [Fact(DisplayName = "Valid phone and six-digit code pass")]
    public void ValidPhoneAndCode_ShouldPass()
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            "123456",
            OtpPurpose.PhoneRegistration);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact(DisplayName = "FirstName and LastName are optional")]
    public void WithoutFirstLastName_ShouldPass()
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            "123456",
            OtpPurpose.PhoneRegistration);

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact(DisplayName = "Names at maximum length pass")]
    public void NameAtMaxLength_ShouldPass()
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            "123456",
            OtpPurpose.PhoneRegistration,
            new string('A', 100),
            new string('B', 100));

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory(DisplayName = "Invalid code fails")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("ABCDEF")]
    [InlineData("12345A")]
    [InlineData("123 56")]
    public void InvalidCode_ShouldFail(string code)
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            code,
            OtpPurpose.PhoneRegistration);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact(DisplayName = "Invalid purpose fails")]
    public void InvalidPurpose_ShouldFail()
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            "123456",
            (OtpPurpose)999);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Purpose);
    }

    [Fact(DisplayName = "FirstName longer than 100 chars fails")]
    public void FirstNameTooLong_ShouldFail()
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            "123456",
            OtpPurpose.PhoneRegistration,
            new string('A', 101),
            "Valid");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact(DisplayName = "LastName longer than 100 chars fails")]
    public void LastNameTooLong_ShouldFail()
    {
        var command = new VerifyPhoneOtpCommand(
            "+491701234567",
            "123456",
            OtpPurpose.PhoneRegistration,
            "Valid",
            new string('B', 101));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }
}

[Trait("Category", "Validators")]
[Trait("Validator", "AddEmail")]
public sealed class AddEmailCommandValidatorTests
{
    private readonly AddEmailCommandValidator _validator = new();

    private static AddEmailCommand Valid(Guid? userId = null, string email = "test@example.com") =>
        new(userId ?? Guid.NewGuid(), email);

    [Theory(DisplayName = "Valid email passes")]
    [InlineData("user@example.com")]
    [InlineData("ahmed.shamri@bizorealestateworld.com")]
    [InlineData("test+tag@domain.org")]
    public void ValidEmail_ShouldPass(string email)
    {
        var result = _validator.TestValidate(Valid(email: email));
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Theory(DisplayName = "Invalid email fails")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("@nodomain.com")]
    [InlineData("no-at-sign.com")]
    [InlineData("spaces in@email.com")]
    public void InvalidEmailFormat_ShouldFail(string email)
    {
        var result = _validator.TestValidate(Valid(email: email));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact(DisplayName = "Email longer than 320 chars fails")]
    public void EmailTooLong_ShouldFail()
    {
        var local = new string('a', 309);
        var email = $"{local}@example.com";

        Assert.True(email.Length > 320);

        var result = _validator.TestValidate(Valid(email: email));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact(DisplayName = "Empty UserId fails")]
    public void EmptyGuidUserId_ShouldFail()
    {
        var result = _validator.TestValidate(Valid(userId: Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact(DisplayName = "Valid UserId passes")]
    public void ValidGuidUserId_ShouldPass()
    {
        var result = _validator.TestValidate(Valid(userId: Guid.NewGuid()));
        result.ShouldNotHaveValidationErrorFor(x => x.UserId);
    }
}

[Trait("Category", "Validators")]
[Trait("Validator", "VerifyEmail")]
public sealed class VerifyEmailCommandValidatorTests
{
    private readonly VerifyEmailCommandValidator _validator = new();

    private static VerifyEmailCommand Valid(Guid? userId = null, string token = "CfDJ8ValidToken1234567") =>
        new(userId ?? Guid.NewGuid(), token);

    [Fact(DisplayName = "Valid UserId and Token pass")]
    public void ValidUserIdAndToken_ShouldPass()
    {
        var result = _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact(DisplayName = "Token with minimum length passes")]
    public void TokenAtMinimumLength_ShouldPass()
    {
        var result = _validator.TestValidate(Valid(token: "1234567890"));
        result.ShouldNotHaveValidationErrorFor(x => x.Token);
    }

    [Fact(DisplayName = "Empty UserId fails")]
    public void EmptyGuid_ShouldFail()
    {
        var result = _validator.TestValidate(Valid(userId: Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Theory(DisplayName = "Invalid token fails")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789")]
    public void InvalidToken_ShouldFail(string token)
    {
        var result = _validator.TestValidate(Valid(token: token));
        result.ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Fact(DisplayName = "Long Identity token passes")]
    public void LongToken_ShouldPass()
    {
        var result = _validator.TestValidate(Valid(token: new string('A', 500)));
        result.ShouldNotHaveValidationErrorFor(x => x.Token);
    }
}
