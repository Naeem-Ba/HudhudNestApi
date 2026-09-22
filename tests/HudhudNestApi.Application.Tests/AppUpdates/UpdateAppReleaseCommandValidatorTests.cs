using HudhudNestApi.Application.AppUpdates.Commands.UpdateAppRelease;

namespace HudhudNestApi.Application.Tests.AppUpdates;

public sealed class UpdateAppReleaseCommandValidatorTests
{
    private readonly UpdateAppReleaseCommandValidator _validator = new();

    private static UpdateAppReleaseCommand ValidCommand(
        string version = "1.1.0", string minimumSupportedVersion = "1.0.0", string? storeUrl = null) => new(
        Guid.NewGuid(), version, minimumSupportedVersion, storeUrl,
        "ملاحظات", "notes", "hinweise", DateTime.UtcNow, Guid.NewGuid(), "127.0.0.1");

    [Fact]
    public void Valid_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand()).IsValid);
    }

    [Fact]
    public void InvalidVersion_Fails()
    {
        Assert.False(_validator.Validate(ValidCommand(version: "not-a-version")).IsValid);
    }

    [Fact]
    public void MinimumGreaterThanVersion_Fails()
    {
        Assert.False(_validator.Validate(ValidCommand(version: "1.0.0", minimumSupportedVersion: "1.1.0")).IsValid);
    }

    [Fact]
    public void EmptyStoreUrl_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand(storeUrl: null)).IsValid);
    }

    [Fact]
    public void MalformedStoreUrl_Fails()
    {
        Assert.False(_validator.Validate(ValidCommand(storeUrl: "not-a-url")).IsValid);
    }
}
