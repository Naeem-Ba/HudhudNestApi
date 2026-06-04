// ============================================================
// PropertyApi.Tests/Validators/RegisterUserCommandValidatorTests.cs
//
// اختبارات وحدة لـ RegisterUserCommandValidator
// ============================================================

using FluentValidation.TestHelper;
using PropertyApi.Application.Users.Commands.RegisterUser;

namespace PropertyApi.Tests.Validators;

public sealed class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _validator = new();

    private static RegisterUserCommand ValidCommand() => new()
    {
        FirstName = "أحمد",
        LastName = "الشمري",
        Email = "ahmed@example.com",
        Password = "SecureP@ss1",
        ConfirmPassword = "SecureP@ss1",
        PreferredLanguage = "ar",
        PreferredCurrency = "EUR"
    };

    // ── نجاح ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Validators")]
    public void ValidCommand_ShouldPass()
    {
        _validator.TestValidate(ValidCommand())
                  .ShouldNotHaveAnyValidationErrors();
    }

    // ── فشل ──────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Validators")]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("")]
    public void InvalidEmail_ShouldFail(string email)
    {
        var cmd = ValidCommand() with { Email = email };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void PasswordTooShort_ShouldFail()
    {
        var cmd = ValidCommand() with { Password = "Ab1", ConfirmPassword = "Ab1" };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void PasswordMismatch_ShouldFail()
    {
        var cmd = ValidCommand() with { ConfirmPassword = "DifferentPass1!" };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.ConfirmPassword);
    }

    [Fact]
    [Trait("Category", "Validators")]
    public void EmptyFirstName_ShouldFail()
    {
        var cmd = ValidCommand() with { FirstName = "" };
        _validator.TestValidate(cmd)
                  .ShouldHaveValidationErrorFor(x => x.FirstName);
    }
}