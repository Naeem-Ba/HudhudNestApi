using FluentValidation.TestHelper;

namespace PropertyApi.Auth.Tests.Application.Validators;

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
