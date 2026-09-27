using PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;

namespace PropertyApi.Application.Tests.SocialDistribution;

/// <summary>
/// Phase 1 audit finding (discovered writing the SocialDistribution E2E suite, not in the
/// original code-reading pass): <c>CreatedByUserId</c> used to require <c>NotEmpty()</c>, but
/// <see cref="Services.DistributionEngine"/> deliberately passes <see cref="Guid.Empty"/> for
/// every system-triggered run (no acting admin) — so the validator rejected it, and
/// DistributionEngine.RunAsync silently created zero publications for every reconciliation sweep
/// and every admin-published property, always. <see cref="Services.DistributionEngine"/>'s own
/// remarks already documented Guid.Empty as the intended "system, not a person" marker; the
/// validator just never matched that convention.
/// </summary>
public sealed class CreateSocialPublicationCommandValidatorTests
{
    private readonly CreateSocialPublicationCommandValidator _validator = new();

    private static CreateSocialPublicationCommand MakeCommand(Guid createdByUserId) => new(
        Guid.NewGuid(), Guid.NewGuid(), createdByUserId, null, null, null, null, "ar");

    [Fact]
    public void CreatedByUserId_GuidEmpty_IsValid_TheSystemMarker()
    {
        var result = _validator.Validate(MakeCommand(Guid.Empty));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreatedByUserId_RealUser_IsValid()
    {
        var result = _validator.Validate(MakeCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PropertyId_Empty_IsRejected()
    {
        var result = _validator.Validate(MakeCommand(Guid.Empty) with { PropertyId = Guid.Empty });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SocialAccountId_Empty_IsRejected()
    {
        var result = _validator.Validate(MakeCommand(Guid.Empty) with { SocialAccountId = Guid.Empty });

        Assert.False(result.IsValid);
    }
}
