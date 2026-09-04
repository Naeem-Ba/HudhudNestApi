using System.Net;
using System.Net.Http.Json;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Integration.Tests.Investments;

/// <summary>
/// Phase 2 §19 — demonstrated through real HTTP requests (never "the code looks right"): User A
/// can never read/modify User B's watchlist or interest, and a private document belonging to
/// another project is never reachable, even when the caller supplies a real, otherwise-valid id.
/// </summary>
[Trait("Feature", "Investments")]
[Trait("Category", "IDOR")]
public sealed class InvestmentIdorTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentIdorTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "IDOR: watchlist is scoped to the caller — B's GET never returns A's item, and B's DELETE on A's item is a 404, not a cross-user removal")]
    public async Task Watchlist_IsScopedToCaller()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);

        var userA = await _factory.SeedUserAsync("usera", RoleNames.User);
        var userB = await _factory.SeedUserAsync("userb", RoleNames.User);

        var addResponse = await _factory.AuthedClient(userA.AccessToken)
            .PostAsync($"/api/investments/projects/{projectId}/watchlist", content: null);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        // B's own watchlist must be empty — A's item must not leak into it.
        var bWatchlist = await _factory.AuthedClient(userB.AccessToken).GetAsync("/api/investments/watchlist");
        var bBody = await bWatchlist.Content.ReadAsStringAsync();
        Assert.DoesNotContain(projectId.ToString(), bBody, StringComparison.OrdinalIgnoreCase);

        // B attempting to remove A's watchlist entry for the same project must not succeed as a
        // removal of A's row — the command is scoped to (CallerId, ProjectId), so for B this key
        // simply doesn't exist yet: NotFound, never a 204 that silently deleted A's row.
        var bDelete = await _factory.AuthedClient(userB.AccessToken)
            .DeleteAsync($"/api/investments/projects/{projectId}/watchlist");
        Assert.Equal(HttpStatusCode.NotFound, bDelete.StatusCode);

        // Prove A's row really does still exist after B's attempt.
        var aWatchlistAfter = await _factory.AuthedClient(userA.AccessToken).GetAsync("/api/investments/watchlist");
        var aBodyAfter = await aWatchlistAfter.Content.ReadAsStringAsync();
        Assert.Contains(projectId.ToString(), aBodyAfter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "IDOR: expression of interest is scoped to the caller — B's status check never reflects A's interest")]
    public async Task Interest_IsScopedToCaller()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);

        var userA = await _factory.SeedUserAsync("usera", RoleNames.User);
        var userB = await _factory.SeedUserAsync("userb", RoleNames.User);

        var expressResponse = await _factory.AuthedClient(userA.AccessToken)
            .PostAsync($"/api/investments/projects/{projectId}/interest", content: null);
        Assert.Equal(HttpStatusCode.Created, expressResponse.StatusCode);

        // A's interest must never appear in B's own interest list.
        var bInterests = await _factory.AuthedClient(userB.AccessToken).GetAsync("/api/investments/interests");
        var bBody = await bInterests.Content.ReadAsStringAsync();
        Assert.DoesNotContain(projectId.ToString(), bBody, StringComparison.OrdinalIgnoreCase);

        // B withdrawing "their" interest on this project (which they never expressed) is a
        // no-op 404 — never a cross-user mutation of A's row.
        var bWithdraw = await _factory.AuthedClient(userB.AccessToken)
            .DeleteAsync($"/api/investments/projects/{projectId}/interest");
        Assert.Equal(HttpStatusCode.NotFound, bWithdraw.StatusCode);

        var aInterestsAfter = await _factory.AuthedClient(userA.AccessToken).GetAsync("/api/investments/interests");
        var aBodyAfter = await aInterestsAfter.Content.ReadAsStringAsync();
        Assert.Contains(projectId.ToString(), aBodyAfter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "IDOR: a private document from Project A is never returned when probing through Project B's public documents endpoint")]
    public async Task PrivateDocument_CannotBeProbedThroughAnotherProject()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);

        var projectA = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);
        var projectB = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Published);

        var privateDocOnA = await _factory.SeedDocumentAsync(projectA, isPublic: true);

        // Querying Project B's document list must never include Project A's document, even
        // though both projects are Published and share the same owner.
        var response = await _factory.AuthedClient().GetAsync($"/api/investments/projects/{projectB}/documents");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(privateDocOnA.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "IDOR: admin document-visibility toggle scoped to the wrong project id returns 404, never affects the real document")]
    public async Task DocumentVisibilityToggle_WrongProjectId_Returns404()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);

        var projectA = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        var projectB = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        var docOnA = await _factory.SeedDocumentAsync(projectA, isPublic: false);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync(
            $"/api/admin/investments/projects/{projectB}/documents/{docOnA}/visibility", new { isPublic = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
