using System.Net;
using System.Net.Http.Json;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Integration.Tests.Investments;

/// <summary>
/// Phase 2 §22/§23 — request-level validation exercised over real HTTP, so a rejected payload
/// is proven to never reach the domain, not just asserted against a FluentValidation validator
/// in isolation.
/// </summary>
[Trait("Feature", "Investments")]
[Trait("Category", "Validation")]
public sealed class InvestmentValidationTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentValidationTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "Create: missing title is rejected with 4xx, never reaches the domain")]
    public async Task Create_MissingTitle_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);

        var response = await _factory.AuthedClient(admin.AccessToken).PostAsJsonAsync("/api/admin/investments/projects", new
        {
            propertyId,
            title = "",
            description = "Valid description.",
            projectType = "Residential",
            currency = "USD",
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Create: invalid/unknown project id (unattached property) is rejected")]
    public async Task Create_InvalidPropertyId_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);

        var response = await _factory.AuthedClient(admin.AccessToken).PostAsJsonAsync("/api/admin/investments/projects", new
        {
            propertyId = Guid.NewGuid(), // does not exist
            title = "Ghost Property Project",
            description = "Valid description.",
            projectType = "Residential",
            currency = "USD",
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Create: malformed enum value for projectType is rejected, not silently defaulted")]
    public async Task Create_UnknownEnumValue_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);

        var response = await _factory.AuthedClient(admin.AccessToken).PostAsync(
            "/api/admin/investments/projects",
            JsonContent(new
            {
                propertyId,
                title = "Bad Enum Project",
                description = "Valid description.",
                projectType = "NotARealProjectType",
                currency = "USD",
            }));

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Update: minimumInvestment > maximumInvestment is rejected")]
    public async Task Update_MinGreaterThanMax_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"/api/admin/investments/projects/{projectId}", new
        {
            title = "Updated Title",
            shortDescription = (string?)null,
            description = "Updated description.",
            projectType = "Residential",
            targetAmount = 100_000m,
            minimumInvestment = 50_000m,
            maximumInvestment = 10_000m, // < minimum
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = (DateOnly?)null,
            endDate = (DateOnly?)null,
            raisedAmount = 0m,
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Update: zero target amount is rejected")]
    public async Task Update_ZeroTargetAmount_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"/api/admin/investments/projects/{projectId}", new
        {
            title = "Updated Title",
            shortDescription = (string?)null,
            description = "Updated description.",
            projectType = "Residential",
            targetAmount = 0m,
            minimumInvestment = 1_000m,
            maximumInvestment = (decimal?)null,
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = (DateOnly?)null,
            endDate = (DateOnly?)null,
            raisedAmount = 0m,
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Update: endDate before startDate is rejected")]
    public async Task Update_EndDateBeforeStartDate_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"/api/admin/investments/projects/{projectId}", new
        {
            title = "Updated Title",
            shortDescription = (string?)null,
            description = "Updated description.",
            projectType = "Residential",
            targetAmount = 100_000m,
            minimumInvestment = 1_000m,
            maximumInvestment = (decimal?)null,
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = new DateOnly(2027, 1, 1),
            endDate = new DateOnly(2026, 1, 1),
            raisedAmount = 0m,
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Update: raisedAmount exceeding targetAmount is rejected")]
    public async Task Update_RaisedAmountExceedsTarget_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"/api/admin/investments/projects/{projectId}", new
        {
            title = "Updated Title",
            shortDescription = (string?)null,
            description = "Updated description.",
            projectType = "Residential",
            targetAmount = 10_000m,
            minimumInvestment = 1_000m,
            maximumInvestment = (decimal?)null,
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = (DateOnly?)null,
            endDate = (DateOnly?)null,
            raisedAmount = 50_000m, // > target
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Financials: a negative cost value is rejected")]
    public async Task Financials_NegativeValue_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"/api/admin/investments/projects/{projectId}/financials", new
        {
            purchasePrice = -1m,
            renovationCost = 0m,
            constructionCost = 0m,
            taxes = 0m,
            notaryCost = 0m,
            brokerCost = 0m,
            financingCost = 0m,
            operatingCost = 0m,
            contingencyReserve = 0m,
            expectedRevenue = 0m,
            expectedProfit = 0m,
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Content field containing guaranteed-return language is rejected (Phase 1 §39 UX rule enforced server-side)")]
    public async Task Update_GuaranteedReturnLanguage_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);

        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync($"/api/admin/investments/projects/{projectId}", new
        {
            title = "Updated Title",
            shortDescription = (string?)null,
            description = "عائد مضمون 100% بدون أي مخاطرة على الإطلاق.",
            projectType = "Residential",
            targetAmount = 100_000m,
            minimumInvestment = 1_000m,
            maximumInvestment = (decimal?)null,
            currency = "USD",
            investmentTermMonths = 12,
            expectedReturnMin = 5m,
            expectedReturnMax = 10m,
            startDate = (DateOnly?)null,
            endDate = (DateOnly?)null,
            raisedAmount = 0m,
        });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Malformed JSON body is rejected with 4xx, not a 500")]
    public async Task MalformedJson_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var client = _factory.AuthedClient(admin.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/investments/projects")
        {
            Content = new StringContent("{ this is not valid json", System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);
        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Calculator: an unknown project id returns 404, not a 500")]
    public async Task Calculator_UnknownProject_Returns404()
    {
        var response = await _factory.AuthedClient().PostAsJsonAsync(
            $"/api/investments/projects/{Guid.NewGuid()}/calculator", new { amount = 1000m, termMonths = 6 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpContent JsonContent(object value) =>
        System.Net.Http.Json.JsonContent.Create(value);
}
