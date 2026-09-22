using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.AppUpdates.Queries.CheckAppUpdate;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.Tests.AppUpdates;

/// <summary>
/// The mandatory-vs-optional decision table. Mandatory is always compared against
/// MinimumSupportedVersion, never against LatestVersion — a newer version merely existing must
/// never force anything by itself.
/// </summary>
public sealed class CheckAppUpdateQueryHandlerTests
{
    private static CheckAppUpdateQueryHandler BuildHandler(EffectiveAppReleaseDto? effective)
    {
        var cache = new Mock<IAppReleaseCacheService>();
        cache.Setup(x => x.GetEffectiveReleaseAsync(It.IsAny<AppPlatform>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(effective);

        return new CheckAppUpdateQueryHandler(cache.Object, NullLogger<CheckAppUpdateQueryHandler>.Instance);
    }

    private static EffectiveAppReleaseDto Release(string version, string minimumSupportedVersion) =>
        new(version, minimumSupportedVersion, "https://example.com/store", "notes-ar", "notes-en", "notes-de", DateTime.UtcNow);

    [Fact]
    public async Task CurrentEqualsLatest_NoUpdateAvailable()
    {
        var handler = BuildHandler(Release("1.0.0", "1.0.0"));
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Android, "1.0.0", null), default);

        Assert.False(result.UpdateAvailable);
        Assert.False(result.Mandatory);
    }

    [Fact]
    public async Task CurrentBelowLatestButAtOrAboveMinimum_OptionalUpdate()
    {
        var handler = BuildHandler(Release("1.1.0", "1.0.0"));
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Android, "1.0.0", null), default);

        Assert.True(result.UpdateAvailable);
        Assert.False(result.Mandatory);
    }

    [Fact]
    public async Task CurrentBelowMinimum_MandatoryUpdate()
    {
        var handler = BuildHandler(Release("1.2.0", "1.1.0"));
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Android, "1.0.0", null), default);

        Assert.True(result.UpdateAvailable);
        Assert.True(result.Mandatory);
    }

    [Fact]
    public async Task CurrentAheadOfLatest_NoUpdateAvailable()
    {
        // e.g. a beta/internal build ahead of the public release train.
        var handler = BuildHandler(Release("1.2.0", "1.0.0"));
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Android, "1.5.0", null), default);

        Assert.False(result.UpdateAvailable);
        Assert.False(result.Mandatory);
    }

    [Fact]
    public async Task NoReleaseConfigured_ReturnsNoUpdateAvailable_AllFieldsNull()
    {
        var handler = BuildHandler(effective: null);
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Web, "1.0.0", null), default);

        Assert.False(result.UpdateAvailable);
        Assert.False(result.Mandatory);
        Assert.Null(result.LatestVersion);
        Assert.Null(result.MinimumSupportedVersion);
        Assert.Null(result.StoreUrl);
        Assert.Null(result.ReleaseNotes);
        Assert.Null(result.ReleaseDate);
    }

    [Fact]
    public async Task CacheServiceThrows_NeverPropagates_ReturnsNoUpdateAvailable()
    {
        var cache = new Mock<IAppReleaseCacheService>();
        cache.Setup(x => x.GetEffectiveReleaseAsync(It.IsAny<AppPlatform>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache and database both unreachable"));

        var handler = new CheckAppUpdateQueryHandler(cache.Object, NullLogger<CheckAppUpdateQueryHandler>.Instance);
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.IOS, "1.0.0", null), default);

        Assert.False(result.UpdateAvailable);
        Assert.False(result.Mandatory);
    }

    [Theory]
    [InlineData("ar", "notes-ar")]
    [InlineData("de", "notes-de")]
    [InlineData("en", "notes-en")]
    [InlineData(null, "notes-en")]
    public async Task Language_SelectsMatchingReleaseNotes_DefaultsToEnglish(string? language, string expected)
    {
        var handler = BuildHandler(Release("1.1.0", "1.0.0"));
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Android, "1.0.0", language), default);

        Assert.Equal(expected, result.ReleaseNotes);
    }

    [Fact]
    public async Task Mandatory_ImpliesUpdateAvailable()
    {
        // Guards the invariant that MinimumSupportedVersion <= Version is enforced at write
        // time, so "below minimum" can never occur without also being "below latest".
        var handler = BuildHandler(Release("2.0.0", "1.5.0"));
        var result = await handler.Handle(new CheckAppUpdateQuery(AppPlatform.Android, "1.0.0", null), default);

        Assert.True(result.Mandatory);
        Assert.True(result.UpdateAvailable);
    }
}
