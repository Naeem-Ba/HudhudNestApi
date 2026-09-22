using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Integration.Tests.Investments;

/// <summary>Phase 2 §24 — document upload/visibility/deletion, exercised over real HTTP against
/// the real AddInvestmentDocumentCommandHandler/RemoveInvestmentDocumentCommandHandler/
/// SetInvestmentDocumentVisibilityCommandHandler pipeline (Cloudinary itself is faked via
/// InvestmentApiTestFactory.Media — no live provider credentials exist in this environment, but
/// routing/auth/validation/EF persistence are all real).</summary>
[Trait("Feature", "Investments")]
[Trait("Category", "DocumentSecurity")]
public sealed class InvestmentDocumentSecurityTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentDocumentSecurityTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "Anonymous cannot upload a document")]
    public async Task Anonymous_UploadDocument_Returns401()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        using var form = BuildUploadForm(InvestmentDocumentType.Other, isPublic: false);
        var response = await _factory.AuthedClient().PostAsync($"/api/admin/investments/projects/{projectId}/documents", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Non-admin authenticated user cannot upload a document")]
    public async Task NonAdmin_UploadDocument_Returns403()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        using var form = BuildUploadForm(InvestmentDocumentType.Other, isPublic: false);
        var response = await _factory.AuthedClient(user.AccessToken).PostAsync($"/api/admin/investments/projects/{projectId}/documents", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Admin can upload a document via real HTTP, and it appears in the admin document list with the correct IsPublic value")]
    public async Task Admin_UploadDocument_Succeeds_AndAppearsInAdminList()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        using var form = BuildUploadForm(InvestmentDocumentType.ProjectPlan, isPublic: false);
        var uploadResponse = await _factory.AuthedClient(admin.AccessToken).PostAsync($"/api/admin/investments/projects/{projectId}/documents", form);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(uploadResponse.StatusCode == HttpStatusCode.Created, $"Expected 201, got {uploadResponse.StatusCode}: {uploadBody}");

        var listResponse = await _factory.AuthedClient(admin.AccessToken).GetAsync($"/api/admin/investments/projects/{projectId}/documents");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains("\"isPublic\":false", listBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Anonymous/non-admin cannot see the admin document list at all (private documents never leak through it)")]
    public async Task DocumentList_AdminOnly()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        await _factory.SeedDocumentAsync(projectId, isPublic: false);

        var anonResponse = await _factory.AuthedClient().GetAsync($"/api/admin/investments/projects/{projectId}/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        var userResponse = await _factory.AuthedClient(user.AccessToken).GetAsync($"/api/admin/investments/projects/{projectId}/documents");
        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);
    }

    [Fact(DisplayName = "Non-admin cannot delete a document")]
    public async Task NonAdmin_DeleteDocument_Returns403()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        var docId = await _factory.SeedDocumentAsync(projectId, isPublic: false);

        var response = await _factory.AuthedClient(user.AccessToken)
            .DeleteAsync($"/api/admin/investments/projects/{projectId}/documents/{docId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Deleting an invalid/unknown document id returns 404, not a 500")]
    public async Task DeleteDocument_UnknownId_Returns404()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken)
            .DeleteAsync($"/api/admin/investments/projects/{projectId}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "Admin can toggle a document's visibility, and the change is immediately reflected on the public endpoint once the project is Published")]
    public async Task Admin_ToggleDocumentVisibility_ReflectsOnPublicEndpoint()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);
        var docId = await _factory.SeedDocumentAsync(projectId, isPublic: false);

        var beforeToggle = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}/documents");
        var beforeBody = await beforeToggle.Content.ReadAsStringAsync();
        Assert.DoesNotContain(docId.ToString(), beforeBody, StringComparison.OrdinalIgnoreCase);

        var toggleResponse = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync(
            $"/api/admin/investments/projects/{projectId}/documents/{docId}/visibility", new { isPublic = true });
        Assert.Equal(HttpStatusCode.NoContent, toggleResponse.StatusCode);

        var afterToggle = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}/documents");
        var afterBody = await afterToggle.Content.ReadAsStringAsync();
        Assert.Contains(docId.ToString(), afterBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Non-admin cannot toggle a document's visibility")]
    public async Task NonAdmin_ToggleDocumentVisibility_Returns403()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        var docId = await _factory.SeedDocumentAsync(projectId, isPublic: false);

        var response = await _factory.AuthedClient(user.AccessToken).PutAsJsonAsync(
            $"/api/admin/investments/projects/{projectId}/documents/{docId}/visibility", new { isPublic = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static MultipartFormDataContent BuildUploadForm(InvestmentDocumentType documentType, bool isPublic)
    {
        var bytes = "%PDF-1.4 fake test content"u8.ToArray();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

        var form = new MultipartFormDataContent
        {
            { fileContent, "file", "test-document.pdf" },
            { new StringContent(documentType.ToString()), "documentType" },
            { new StringContent(isPublic.ToString()), "isPublic" },
        };
        return form;
    }
}
