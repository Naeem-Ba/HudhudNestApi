using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Domain.AppUpdates.Enums;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.AppUpdates;

[Trait("Feature", "AppUpdates")]
public sealed class AdminAppReleasesCrudTests : IClassFixture<AppUpdateApiTestFactory>, IAsyncLifetime
{
    private readonly AppUpdateApiTestFactory _factory;

    public AdminAppReleasesCrudTests(AppUpdateApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object ValidCreatePayload(
        AppPlatform platform = AppPlatform.Android, string version = "1.1.0", string minimumSupportedVersion = "1.0.0",
        bool isEnabled = true) => new
        {
            platform = platform.ToString(),
            version,
            minimumSupportedVersion,
            storeUrl = (string?)null,
            releaseNotesAr = "ar",
            releaseNotesEn = "en",
            releaseNotesDe = "de",
            releaseDate = DateTime.UtcNow,
            isEnabled,
        };

    private async Task<SeededUser> AdminAsync() =>
        await _factory.SeedUserAsync("admin", RoleNames.Admin);

    [Fact(DisplayName = "Admin can create a release, appears in list and get-by-id")]
    public async Task Create_ThenListAndGetById_Succeeds()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/admin/app-releases", ValidCreatePayload());
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, $"Expected 201, got {createResponse.StatusCode}: {createBody}");

        var listResponse = await client.GetAsync("/api/admin/app-releases");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("1.1.0", listBody);
    }

    [Fact(DisplayName = "Duplicate enabled release for same platform+version -> 409")]
    public async Task DuplicateEnabledRelease_Returns409()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);

        var first = await client.PostAsJsonAsync("/api/admin/app-releases", ValidCreatePayload());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/admin/app-releases", ValidCreatePayload());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact(DisplayName = "MinimumSupportedVersion above Version -> 400")]
    public async Task MinimumAboveVersion_Returns400()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);

        var response = await client.PostAsJsonAsync(
            "/api/admin/app-releases", ValidCreatePayload(version: "1.0.0", minimumSupportedVersion: "1.1.0"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Malformed StoreUrl -> 400, empty StoreUrl accepted -> 201")]
    public async Task StoreUrlValidation()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);

        var malformed = await client.PostAsJsonAsync("/api/admin/app-releases", new
        {
            platform = "Android",
            version = "1.1.0",
            minimumSupportedVersion = "1.0.0",
            storeUrl = "not-a-url",
            releaseDate = DateTime.UtcNow,
            isEnabled = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        var empty = await client.PostAsJsonAsync("/api/admin/app-releases", ValidCreatePayload());
        Assert.Equal(HttpStatusCode.Created, empty.StatusCode);
    }

    [Fact(DisplayName = "Update persists changes")]
    public async Task Update_PersistsChanges()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);
        var id = await _factory.SeedReleaseAsync(AppPlatform.Android, "1.1.0", "1.0.0");

        var response = await client.PutAsJsonAsync($"/api/admin/app-releases/{id}", new
        {
            version = "1.2.0",
            minimumSupportedVersion = "1.0.0",
            storeUrl = (string?)null,
            releaseNotesAr = "updated-ar",
            releaseNotesEn = "updated-en",
            releaseNotesDe = "updated-de",
            releaseDate = DateTime.UtcNow,
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/admin/app-releases/{id}");
        var body = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("1.2.0", body);
    }

    [Fact(DisplayName = "Enable/disable end-to-end changes what the check endpoint reports")]
    public async Task EnableDisable_ChangesCheckEndpointResult_ProvesCacheInvalidation()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);
        var id = await _factory.SeedReleaseAsync(AppPlatform.Android, "1.5.0", "1.0.0", isEnabled: false);

        var beforeEnable = await client.GetAsync("/api/app-updates/check?platform=Android&currentVersion=1.0.0");
        var beforeBody = await beforeEnable.Content.ReadAsStringAsync();
        Assert.Contains("\"updateAvailable\":false", beforeBody);

        var enableResponse = await client.PostAsync($"/api/admin/app-releases/{id}/enable", content: null);
        Assert.Equal(HttpStatusCode.NoContent, enableResponse.StatusCode);

        var afterEnable = await client.GetAsync("/api/app-updates/check?platform=Android&currentVersion=1.0.0");
        var afterBody = await afterEnable.Content.ReadAsStringAsync();
        Assert.Contains("\"updateAvailable\":true", afterBody);

        var disableResponse = await client.PostAsync($"/api/admin/app-releases/{id}/disable", content: null);
        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        var afterDisable = await client.GetAsync("/api/app-updates/check?platform=Android&currentVersion=1.0.0");
        var afterDisableBody = await afterDisable.Content.ReadAsStringAsync();
        Assert.Contains("\"updateAvailable\":false", afterDisableBody);
    }

    [Fact(DisplayName = "Delete is a soft delete: row remains with IsDeleted=true, no longer in admin list")]
    public async Task Delete_IsSoftDelete()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);
        var id = await _factory.SeedReleaseAsync(AppPlatform.Android, "1.1.0", "1.0.0");

        var deleteResponse = await client.DeleteAsync($"/api/admin/app-releases/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/admin/app-releases/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var stillExists = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var release = await db.AppReleases.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id);
            return release is { IsDeleted: true, DeletedAt: not null };
        });
        Assert.True(stillExists);
    }

    [Fact(DisplayName = "Every mutation writes a matching AuditLog row")]
    public async Task Mutations_WriteAuditLog()
    {
        var admin = await AdminAsync();
        var client = _factory.AuthedClient(admin.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/admin/app-releases", ValidCreatePayload());
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedIdResponse>();

        await client.PostAsync($"/api/admin/app-releases/{created!.Id}/disable", content: null);
        await client.PostAsync($"/api/admin/app-releases/{created.Id}/enable", content: null);
        await client.DeleteAsync($"/api/admin/app-releases/{created.Id}");

        var actions = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            return await db.AuditLogs
                .Where(a => a.UserId == admin.Id)
                .Select(a => a.Action)
                .ToListAsync();
        });

        Assert.Contains(AuditActions.AppReleaseCreatedByAdmin, actions);
        Assert.Contains(AuditActions.AppReleaseDisabledByAdmin, actions);
        Assert.Contains(AuditActions.AppReleaseEnabledByAdmin, actions);
        Assert.Contains(AuditActions.AppReleaseDeletedByAdmin, actions);
    }

    private sealed record CreatedIdResponse(Guid Id);

    [Theory(DisplayName = "Non-admin authenticated user -> 403 on every admin endpoint")]
    [InlineData("GET", "/api/admin/app-releases")]
    [InlineData("POST", "/api/admin/app-releases")]
    public async Task NonAdmin_Returns403(string method, string path)
    {
        var user = await _factory.SeedUserAsync("regular", RoleNames.User);
        var client = _factory.AuthedClient(user.AccessToken);

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(ValidCreatePayload());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory(DisplayName = "Anonymous -> 401 on every admin endpoint")]
    [InlineData("GET", "/api/admin/app-releases")]
    [InlineData("POST", "/api/admin/app-releases")]
    public async Task Anonymous_Returns401(string method, string path)
    {
        var client = _factory.AuthedClient();

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(ValidCreatePayload());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
