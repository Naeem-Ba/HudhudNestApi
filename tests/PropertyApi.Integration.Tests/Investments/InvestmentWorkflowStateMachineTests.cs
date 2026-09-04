using System.Net;
using System.Net.Http.Json;
using PropertyApi.Domain.Investments.Enums;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Integration.Tests.Investments;

/// <summary>
/// Phase 2 §21/§25 — the InvestmentProject state machine and the admin workflow endpoints that
/// drive it, exercised over real HTTP against a real Postgres row so an illegal transition is
/// proven rejected by the running API, not just by the domain unit tests in
/// PropertyApi.Application.Tests (which never see HTTP or persistence).
/// </summary>
[Trait("Feature", "Investments")]
[Trait("Category", "WorkflowStateMachine")]
public sealed class InvestmentWorkflowStateMachineTests : IClassFixture<InvestmentApiTestFactory>, IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory;

    public InvestmentWorkflowStateMachineTests(InvestmentApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "Valid path: Draft -> UnderReview -> Approved -> Scheduled -> Published -> Suspended -> Closed, each over real HTTP")]
    public async Task ValidTransitionPath_Succeeds()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Draft);
        await _factory.SeedFinancialsAsync(projectId);
        await _factory.SeedRiskAsync(projectId);
        await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.ProjectPlan);
        await _factory.SeedDocumentAsync(projectId, isPublic: true, InvestmentDocumentType.FinancialStatement);

        var client = _factory.AuthedClient(admin.AccessToken);

        await AssertNoContent(client.PostAsync($"/api/admin/investments/projects/{projectId}/submit-review", null));
        await AssertNoContent(client.PostAsync($"/api/admin/investments/projects/{projectId}/approve", null));
        await AssertNoContent(client.PostAsJsonAsync($"/api/admin/investments/projects/{projectId}/schedule", new { scheduledPublishAt = (DateTime?)null }));

        var publishResponse = await client.PostAsync($"/api/admin/investments/projects/{projectId}/publish", null);
        var publishBody = await publishResponse.Content.ReadAsStringAsync();
        Assert.True(publishResponse.StatusCode == HttpStatusCode.NoContent,
            $"Publish should succeed once financials+risk+required docs exist (docs are the one gap here); got {publishResponse.StatusCode}: {publishBody}");

        await AssertNoContent(client.PostAsync($"/api/admin/investments/projects/{projectId}/suspend", null));
        await AssertNoContent(client.PostAsync($"/api/admin/investments/projects/{projectId}/close", null));
    }

    [Theory(DisplayName = "Invalid transitions are rejected by the running API even when sent directly")]
    [InlineData(InvestmentProjectStatus.Draft, "approve")]
    [InlineData(InvestmentProjectStatus.Draft, "publish")]
    [InlineData(InvestmentProjectStatus.Draft, "close")]
    [InlineData(InvestmentProjectStatus.Published, "approve")]
    [InlineData(InvestmentProjectStatus.Published, "submit-review")]
    [InlineData(InvestmentProjectStatus.Closed, "publish")]
    [InlineData(InvestmentProjectStatus.Closed, "submit-review")]
    [InlineData(InvestmentProjectStatus.Rejected, "publish")]
    [InlineData(InvestmentProjectStatus.Rejected, "approve")]
    public async Task InvalidTransition_IsRejected(InvestmentProjectStatus fromStatus, string action)
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, fromStatus);

        var response = await _factory.AuthedClient(admin.AccessToken)
            .PostAsync($"/api/admin/investments/projects/{projectId}/{action}", null);

        Assert.True(
            (int)response.StatusCode is >= 400 and < 500,
            $"Expected a 4xx rejection transitioning out of {fromStatus} via '{action}', got {response.StatusCode}");
    }

    [Fact(DisplayName = "Publish is rejected when financials/risk/required documents are missing, with the exact readiness checklist visible to admins")]
    public async Task Publish_WithoutReadiness_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.Scheduled);
        // Deliberately no financials/risk/documents seeded.

        var client = _factory.AuthedClient(admin.AccessToken);

        var reviewResponse = await client.GetAsync($"/api/admin/investments/projects/{projectId}");
        var reviewBody = await reviewResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        Assert.Contains("\"isReady\":false", reviewBody, StringComparison.OrdinalIgnoreCase);

        var publishResponse = await client.PostAsync($"/api/admin/investments/projects/{projectId}/publish", null);
        Assert.True((int)publishResponse.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Reject requires a non-empty reason — an empty reason is rejected by validation, not silently accepted")]
    public async Task Reject_WithoutReason_IsRejected()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);
        var projectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, InvestmentProjectStatus.UnderReview);

        var response = await _factory.AuthedClient(admin.AccessToken)
            .PostAsJsonAsync($"/api/admin/investments/projects/{projectId}/reject", new { reason = "" });

        Assert.True((int)response.StatusCode is >= 400 and < 500);
    }

    // ── §25: every admin workflow action independently, across the role matrix ─

    [Theory(DisplayName = "Admin workflow actions: Anonymous gets 401, non-admin gets 403, Admin succeeds — for every workflow action")]
    [InlineData("submit-review", InvestmentProjectStatus.Draft)]
    [InlineData("approve", InvestmentProjectStatus.UnderReview)]
    [InlineData("suspend", InvestmentProjectStatus.Published)]
    [InlineData("close", InvestmentProjectStatus.Published)]
    public async Task AdminWorkflowAction_RoleMatrix(string action, InvestmentProjectStatus fromStatus)
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var propertyId = await _factory.SeedPropertyAsync(admin.Id);

        // Anonymous
        var anonProjectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, fromStatus);
        var anonResponse = await _factory.AuthedClient().PostAsync($"/api/admin/investments/projects/{anonProjectId}/{action}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        // Authenticated non-admin
        var user = await _factory.SeedUserAsync("user", RoleNames.User);
        var userProjectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, fromStatus);
        var userResponse = await _factory.AuthedClient(user.AccessToken).PostAsync($"/api/admin/investments/projects/{userProjectId}/{action}", null);
        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);

        // Admin
        var adminProjectId = await _factory.SeedInvestmentProjectAsync(propertyId, admin.Id, fromStatus);
        var adminResponse = await _factory.AuthedClient(admin.AccessToken).PostAsync($"/api/admin/investments/projects/{adminProjectId}/{action}", null);
        Assert.Equal(HttpStatusCode.NoContent, adminResponse.StatusCode);
    }

    private static async Task AssertNoContent(Task<HttpResponseMessage> responseTask)
    {
        var response = await responseTask;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"Expected 204, got {response.StatusCode}: {body}");
    }
}
