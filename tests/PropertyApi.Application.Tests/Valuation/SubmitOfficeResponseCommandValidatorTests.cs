using FluentValidation.TestHelper;
using PropertyApi.Application.Valuation.Commands.SubmitOfficeResponse;

namespace PropertyApi.Application.Tests.Valuation;

/// <summary>
/// Remediation L4 — explicit boundary coverage for
/// SubmitOfficeResponseCommandValidator.Notes' MaximumLength(2000) rule (correct as-is; this
/// only closes a gap in test coverage, per the audit's own finding that the rule existed but
/// had no boundary-exact test). Same TestValidateAsync/ShouldHaveValidationErrorFor style as
/// CreatePropertyCommandValidatorTests' own Title/Description/City boundary tests.
/// </summary>
public sealed class SubmitOfficeResponseCommandValidatorTests
{
    private readonly SubmitOfficeResponseCommandValidator _validator = new();

    [Fact]
    public async Task Notes_ExactlyAtTheLimit_2000Characters_PassesValidation()
    {
        var command = ValidCommand() with { Notes = new string('س', 2000) };

        (await _validator.TestValidateAsync(command))
            .ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public async Task Notes_OneCharacterOverTheLimit_2001Characters_FailsValidation()
    {
        var command = ValidCommand() with { Notes = new string('س', 2001) };

        (await _validator.TestValidateAsync(command))
            .ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public async Task Notes_Null_PassesValidation()
    {
        // Notes is optional (the .When(x => x.Notes is not null) guard on the rule) — null
        // must never itself be treated as "too long".
        var command = ValidCommand() with { Notes = null };

        (await _validator.TestValidateAsync(command))
            .ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public async Task EstimatedPrice_Zero_FailsValidation()
    {
        var command = ValidCommand() with { EstimatedPrice = 0m };

        (await _validator.TestValidateAsync(command))
            .ShouldHaveValidationErrorFor(x => x.EstimatedPrice);
    }

    [Fact]
    public async Task EstimatedPrice_Negative_FailsValidation()
    {
        var command = ValidCommand() with { EstimatedPrice = -1m };

        (await _validator.TestValidateAsync(command))
            .ShouldHaveValidationErrorFor(x => x.EstimatedPrice);
    }

    [Fact]
    public async Task EstimatedPrice_Positive_PassesValidation()
    {
        var command = ValidCommand() with { EstimatedPrice = 1m };

        (await _validator.TestValidateAsync(command))
            .ShouldNotHaveValidationErrorFor(x => x.EstimatedPrice);
    }

    private static SubmitOfficeResponseCommand ValidCommand()
        => new(
            InvitationId: Guid.NewGuid(),
            ActorUserId: Guid.NewGuid(),
            EstimatedPrice: 100000m,
            Notes: null);
}
