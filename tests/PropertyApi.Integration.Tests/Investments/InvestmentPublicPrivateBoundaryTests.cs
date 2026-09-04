using System.Net;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Integration.Tests.Investments;

/// <summary>
/// Phase 2 §20 — exactly what an anonymous caller can see. A Draft/UnderReview/Approved/
/// Scheduled project must never be reachable by id through the public endpoints, even though
/// the public list endpoint itself only ever returns Published rows; probing a non-Published id
/// directly is the actual attack this proves closed (Phase 1 §20 IDOR-shaped gap).
/// </summary>
[Trait("Feature", "Investments")]
[Trait("Category", "PublicPrivateBoundary")]
public sealed class InvestmentPublicPrivateBoundaryTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentPublicPrivateBoundaryTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory(DisplayName = "A non-Published project is never reachable by id through the public detail endpoint")]
    [InlineData(InvestmentProjectStatus.Draft)]
    [InlineData(InvestmentProjectStatus.UnderReview)]
    [InlineData(InvestmentProjectStatus.Approved)]
    [InlineData(InvestmentProjectStatus.Scheduled)]
    [InlineData(InvestmentProjectStatus.Rejected)]
    public async Task NonPublished_ProjectDetail_Returns404(InvestmentProjectStatus status)
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, status);

        var response = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "A Published project IS reachable by id through the public detail endpoint")]
    public async Task Published_ProjectDetail_Returns200()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);

        var response = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "A non-Published project's list row is never returned in the public list, even filtered by its own id-adjacent title")]
    public async Task NonPublished_Project_NeverAppearsInPublicList()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient().GetAsync("/api/investments/projects?pageSize=100");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Draft", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "Financials/risk on a non-Published project are never exposed, even when the data exists")]
    [InlineData("financials")]
    [InlineData("risk")]
    public async Task NonPublished_FinancialsAndRisk_Returns404EvenWhenDataExists(string subResource)
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.UnderReview);
        await _factory.SeedFinancialsAsync(projectId);
        await _factory.SeedRiskAsync(projectId);

        var response = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}/{subResource}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "A private (IsPublic=false) document on a Published project is never returned by the public documents endpoint")]
    public async Task PrivateDocument_OnPublishedProject_NeverReturnedPublicly()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);
        var privateDocId = await _factory.SeedDocumentAsync(projectId, isPublic: false);
        var publicDocId = await _factory.SeedDocumentAsync(projectId, isPublic: true);

        var response = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}/documents");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(privateDocId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(publicDocId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "A public document on a non-Published project is still never returned publicly (project status gate wins)")]
    public async Task PublicDocument_OnNonPublishedProject_NeverReturnedPublicly()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Approved);
        var docId = await _factory.SeedDocumentAsync(projectId, isPublic: true);

        var response = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectId}/documents");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(docId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }
}
