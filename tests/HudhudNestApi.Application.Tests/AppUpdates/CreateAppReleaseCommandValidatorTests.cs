using HudhudNestApi.Application.AppUpdates.Commands.CreateAppRelease;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.Tests.AppUpdates;

public sealed class CreateAppReleaseCommandValidatorTests
{
    private readonly CreateAppReleaseCommandValidator _validator = new();

    private const string PlayUrl = "https://play.google.com/store/apps/details?id=com.hudhudnest.app";

    private static CreateAppReleaseCommand ValidCommand(
        string version = "1.1.0", string minimumSupportedVersion = "1.0.0", string? storeUrl = PlayUrl,
        AppPlatform platform = AppPlatform.Android, bool isEnabled = true) => new(
        platform, version, minimumSupportedVersion, storeUrl,
        "ملاحظات", "notes", "hinweise", DateTime.UtcNow, IsEnabled: isEnabled,
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
    public void EmptyStoreUrl_Passes_WhenTheReleaseIsCreatedDisabled()
    {
        Assert.True(_validator.Validate(ValidCommand(storeUrl: null, isEnabled: false)).IsValid);
    }

    [Theory]
    [InlineData(AppPlatform.Android)]
    [InlineData(AppPlatform.IOS)]
    public void EnabledStoreRelease_WithoutStoreUrl_Fails(AppPlatform platform)
    {
        Assert.False(_validator.Validate(ValidCommand(storeUrl: null, platform: platform)).IsValid);
        Assert.False(_validator.Validate(ValidCommand(storeUrl: "  ", platform: platform)).IsValid);
    }

    [Fact]
    public void EnabledWebRelease_WithoutStoreUrl_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand(storeUrl: null, platform: AppPlatform.Web)).IsValid);
    }

    [Theory]
    [InlineData(AppPlatform.Android, "https://play.google.com/store/apps/details?id=x", true)]
    [InlineData(AppPlatform.Android, "https://PLAY.GOOGLE.COM/store/apps/details?id=x", true)]
    [InlineData(AppPlatform.Android, "https://apps.apple.com/app/id123", false)]
    [InlineData(AppPlatform.Android, "http://play.google.com/store/apps/details?id=x", false)]
    [InlineData(AppPlatform.Android, "https://play.google.com.evil.example/store/apps/details?id=x", false)]
    [InlineData(AppPlatform.Android, "https://evil.example/play.google.com", false)]
    [InlineData(AppPlatform.Android, "https://user@play.google.com/store/apps/details?id=x", false)]
    [InlineData(AppPlatform.Android, "https://play.google.com:8443/store/apps/details?id=x", false)]
    [InlineData(AppPlatform.IOS, "https://apps.apple.com/app/id123", true)]
    [InlineData(AppPlatform.IOS, "https://itunes.apple.com/app/id123", true)]
    [InlineData(AppPlatform.IOS, "https://play.google.com/store/apps/details?id=x", false)]
    [InlineData(AppPlatform.Web, "https://hudhudnest.com", true)]
    [InlineData(AppPlatform.Web, "http://hudhudnest.com", false)]
    public void StoreUrl_MustBeTheOfficialStoreOfThePlatform(AppPlatform platform, string url, bool valid)
    {
        Assert.Equal(valid, _validator.Validate(ValidCommand(storeUrl: url, platform: platform)).IsValid);
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
