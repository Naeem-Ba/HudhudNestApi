using HudhudNestApi.Application.AppUpdates.Commands.CreateAppRelease;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.Tests.AppUpdates;

public sealed class CreateAppReleaseCommandValidatorTests
{
    private readonly CreateAppReleaseCommandValidator _validator = new();

    private static CreateAppReleaseCommand ValidCommand(
        string version = "1.1.0", string minimumSupportedVersion = "1.0.0", string? storeUrl = null) => new(
        AppPlatform.Android, version, minimumSupportedVersion, storeUrl,
        "ملاحظات", "notes", "hinweise", DateTime.UtcNow, IsEnabled: true,
        Guid.NewGuid(), "127.0.0.1");

    [Fact]
    public void Valid_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand()).IsValid);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.2")]
    [InlineData("")]
    public void InvalidVersion_Fails(string version)
    {
        Assert.False(_validator.Validate(ValidCommand(version: version)).IsValid);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.2")]
    public void InvalidMinimumSupportedVersion_Fails(string minimum)
    {
        Assert.False(_validator.Validate(ValidCommand(minimumSupportedVersion: minimum)).IsValid);
    }

    [Fact]
    public void MinimumGreaterThanVersion_Fails()
    {
        var command = ValidCommand(version: "1.0.0", minimumSupportedVersion: "1.1.0");
        Assert.False(_validator.Validate(command).IsValid);
    }

    [Fact]
    public void MinimumEqualToVersion_Passes()
    {
        var command = ValidCommand(version: "1.0.0", minimumSupportedVersion: "1.0.0");
        Assert.True(_validator.Validate(command).IsValid);
    }

    [Fact]
    public void EmptyStoreUrl_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand(storeUrl: null)).IsValid);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    public void MalformedStoreUrl_Fails(string storeUrl)
    {
        Assert.False(_validator.Validate(ValidCommand(storeUrl: storeUrl)).IsValid);
    }

    [Fact]
    public void ValidAbsoluteHttpsStoreUrl_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand(storeUrl: "https://play.google.com/store/apps/details?id=com.example")).IsValid);
    }
}
