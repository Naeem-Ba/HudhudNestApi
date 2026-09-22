using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Integration.Tests.Investments;

[Trait("Feature", "Investments")]
public sealed class InvestmentSmokeTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentSmokeTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "Anonymous GET /api/investments/projects returns 200 over real HTTP")]
    public async Task AnonymousList_Returns200()
    {
        var client = _factory.AuthedClient();
        var response = await client.GetAsync("/api/investments/projects");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200, got {response.StatusCode}: {body}");
    }

    [Fact(DisplayName = "Admin can create a project via real HTTP end to end")]
    public async Task AdminCreateProject_Returns201()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var client = _factory.AuthedClient(admin.AccessToken);

        var response = await client.PostAsJsonAsync("/api/admin/investments/projects", new
        {
            propertyId,
            title = "Smoke Test Project",
            description = "Smoke test description.",
            projectType = "Residential",
            currency = "USD",
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201, got {response.StatusCode}: {body}");
    }
}
