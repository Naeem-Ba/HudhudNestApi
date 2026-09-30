using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Integration.Tests.Investments;

/// <summary>
/// Phase 2 §18 — real HTTP authorization matrix (Anonymous / authenticated non-admin / Admin)
/// across representative public, authenticated, and admin-only Investment endpoints. Every
/// assertion here hits the actual running host, not a mocked ISender.
/// </summary>
[Trait("Feature", "Investments")]
[Trait("Category", "Authorization")]
public sealed class InvestmentAuthorizationMatrixTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentAuthorizationMatrixTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Public endpoints: anonymous must succeed ───────────────────

    [Fact(DisplayName = "Anonymous: public project list is reachable without a token")]
    public async Task Anonymous_PublicList_Returns200()
    {
        var response = await _factory.AuthedClient().GetAsync("/api/investments/projects");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Anonymous: calculator on a published project is reachable without a token")]
    public async Task Anonymous_Calculator_Returns200()
    {
        var owner = await _factory.SeedUserAsync("owner", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, owner.Id, InvestmentProjectStatus.Published);

        var response = await _factory.AuthedClient().PostAsJsonAsync(
            $"/api/investments/projects/{projectId}/calculator", new { amount = 5000m, termMonths = 12 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Authenticated-only endpoints: anonymous must be rejected ───

    [Theory(DisplayName = "Anonymous: authenticated-only Investment endpoints reject without a token")]
    [InlineData("GET", "/api/investments/watchlist")]
    [InlineData("GET", "/api/investments/interests")]
    public async Task Anonymous_AuthenticatedOnly_Returns401(string method, string path)
    {
        var client = _factory.AuthedClient();
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Anonymous: adding to watchlist without a token is rejected")]
    public async Task Anonymous_AddToWatchlist_Returns401()
    {
        var response = await _factory.AuthedClient().PostAsync($"/api/investments/projects/{Guid.NewGuid()}/watchlist", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Anonymous: expressing interest without a token is rejected")]
    public async Task Anonymous_ExpressInterest_Returns401()
    {
        var response = await _factory.AuthedClient().PostAsync($"/api/investments/projects/{Guid.NewGuid()}/interest", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Authenticated (non-admin) can use their own watchlist/interest ─

    [Fact(DisplayName = "Authenticated non-admin: can read their own (empty) watchlist")]
    public async Task AuthenticatedUser_OwnWatchlist_Returns200()
    {
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var response = await _factory.AuthedClient(user.AccessToken).GetAsync("/api/investments/watchlist");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Admin-only endpoints: anonymous and non-admin must both be rejected ─

    [Fact(DisplayName = "Anonymous: admin project list is rejected without a token")]
    public async Task Anonymous_AdminList_Returns401()
    {
        var response = await _factory.AuthedClient().GetAsync("/api/admin/investments/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Authenticated non-admin: admin project list is rejected with 403")]
    public async Task NonAdmin_AdminList_Returns403()
    {
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var response = await _factory.AuthedClient(user.AccessToken).GetAsync("/api/admin/investments/projects");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Authenticated non-admin: creating a project via the admin API is rejected with 403")]
    public async Task NonAdmin_CreateProject_Returns403()
    {
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var propertyId = await _factory.SeedPropertyAsync(user.Id);

        var response = await _factory.AuthedClient(user.AccessToken).PostAsJsonAsync("/api/admin/investments/projects", new
        {
            propertyId,
            title = "Should Not Be Created",
            description = "Non-admin attempt.",
            projectType = "Residential",
            currency = "USD",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Admin: admin project list succeeds with a real Admin-role token")]
    public async Task Admin_AdminList_Returns200()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var response = await _factory.AuthedClient(admin.AccessToken).GetAsync("/api/admin/investments/projects");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
