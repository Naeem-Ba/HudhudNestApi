using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Domain.AppUpdates.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Integration.Tests.AppUpdates;

[Trait("Feature", "AppUpdates")]
public sealed class AppUpdateCheckEndpointTests : IClassFixture<AppUpdateApiTestFactory>, IAsyncLifetime
{
    private readonly AppUpdateApiTestFactory _factory;

    public AppUpdateCheckEndpointTests(AppUpdateApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record CheckResponse(
        bool UpdateAvailable, bool Mandatory, string? LatestVersion, string? MinimumSupportedVersion,
        string? StoreUrl, string? ReleaseNotes, DateTime? ReleaseDate);

    [Fact(DisplayName = "No release configured -> 200 with updateAvailable=false, not an error")]
    public async Task NoReleaseConfigured_ReturnsNoUpdateAvailable()
    {
        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/app-updates/check?platform=Web&currentVersion=1.0.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.NotNull(body);
        Assert.False(body!.UpdateAvailable);
        Assert.False(body.Mandatory);
        Assert.Null(body.LatestVersion);
    }

    [Fact(DisplayName = "Version above current but at/above minimum -> optional update")]
    public async Task NewerVersionAtOrAboveMinimum_OptionalUpdate()
    {
        await _factory.SeedReleaseAsync(AppPlatform.Android, "1.2.0", "1.0.0");

        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/app-updates/check?platform=Android&currentVersion=1.1.0");
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();

        Assert.True(body!.UpdateAvailable);
        Assert.False(body.Mandatory);
        Assert.Equal("1.2.0", body.LatestVersion);
    }

    [Fact(DisplayName = "Version below minimum -> mandatory update")]
    public async Task VersionBelowMinimum_MandatoryUpdate()
    {
        await _factory.SeedReleaseAsync(AppPlatform.Android, "1.2.0", "1.1.0");

        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/app-updates/check?platform=Android&currentVersion=1.0.0");
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();

        Assert.True(body!.UpdateAvailable);
        Assert.True(body.Mandatory);
    }

    [Fact(DisplayName = "Disabled release is ignored")]
    public async Task DisabledRelease_Ignored()
    {
        await _factory.SeedReleaseAsync(AppPlatform.IOS, "1.5.0", "1.0.0", isEnabled: false);

        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/app-updates/check?platform=IOS&currentVersion=1.0.0");
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();

        Assert.False(body!.UpdateAvailable);
        Assert.Null(body.LatestVersion);
    }

    [Fact(DisplayName = "Invalid currentVersion -> 422")]
    public async Task InvalidVersion_Returns422()
    {
        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/app-updates/check?platform=Android&currentVersion=not-a-version");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact(DisplayName = "Invalid platform -> 400")]
    public async Task InvalidPlatform_Returns400()
    {
        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/app-updates/check?platform=NotAPlatform&currentVersion=1.0.0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Same response shape for anonymous and authenticated callers")]
    public async Task AnonymousAndAuthenticated_GetIdenticalResponse()
    {
        await _factory.SeedReleaseAsync(AppPlatform.Web, "2.0.0", "1.0.0");
        var user = await _factory.SeedUserAsync("regular", RoleNames.User);

        var anonymousResponse = await _factory.AuthedClient().GetAsync("/api/app-updates/check?platform=Web&currentVersion=1.0.0");
        var authedResponse = await _factory.AuthedClient(user.AccessToken).GetAsync("/api/app-updates/check?platform=Web&currentVersion=1.0.0");

        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authedResponse.StatusCode);

        var anonymousBody = await anonymousResponse.Content.ReadFromJsonAsync<CheckResponse>();
        var authedBody = await authedResponse.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.Equal(anonymousBody, authedBody);
    }

    [Theory(DisplayName = "lang selects the matching release-notes column")]
    [InlineData("ar", "ar-notes")]
    [InlineData("en", "en-notes")]
    [InlineData("de", "de-notes")]
    public async Task Lang_SelectsMatchingReleaseNotes(string lang, string expectedNotes)
    {
        await _factory.SeedReleaseAsync(AppPlatform.Android, "1.5.0", "1.0.0");

        var client = _factory.AuthedClient();
        var response = await client.GetAsync($"/api/app-updates/check?platform=Android&currentVersion=1.0.0&lang={lang}");
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();

        Assert.Equal(expectedNotes, body!.ReleaseNotes);
    }
}
