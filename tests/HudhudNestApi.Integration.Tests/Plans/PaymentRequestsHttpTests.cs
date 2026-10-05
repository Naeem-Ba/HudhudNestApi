using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Plans;

/// <summary>
/// End to end over real HTTP and real Postgres: a paid plan or a featured placement is only a
/// request until an administrator confirms payment. Covers the new <c>PlanRequests</c> table,
/// the admin queue read model and the owner's "pending featured" read.
/// </summary>
[Trait("Feature", "PaymentRequests")]
public sealed class PaymentRequestsHttpTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();

        // PrepareDatabaseAsync truncates the catalog tables the plan and fee flows read
        // (Plans, Currencies); put them back the way a fresh deployment has them.
        await _factory.InScopeAsync(services => DatabaseSeeder.SeedAsync(services));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact(DisplayName = "A paid plan is a pending request: the account keeps its plan until an admin approves")]
    public async Task PaidPlan_IsPending_UntilAdminApproves()
    {
        var admin = await _factory.SeedUserAsync("plan-admin", RoleNames.Admin);
        var user = await _factory.SeedUserAsync("plan-user");
        var userClient = _factory.AuthedClient(user.AccessToken);
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        var select = await userClient.PostAsJsonAsync("/api/users/me/plan", new { tier = "premium" });
        Assert.Equal(HttpStatusCode.Accepted, select.StatusCode);
        var accepted = await ReadJsonAsync(select);
        Assert.Equal("pending", accepted.GetProperty("status").GetString());
        var requestId = accepted.GetProperty("requestId").GetGuid();

        // Asking again for the same tier returns the same request, not a second row.
        var again = await userClient.PostAsJsonAsync("/api/users/me/plan", new { tier = "premium" });
        Assert.Equal(requestId, (await ReadJsonAsync(again)).GetProperty("requestId").GetGuid());

        var me = await ReadJsonAsync(await userClient.GetAsync("/api/users/me"));
        Assert.Equal("premium", me.GetProperty("pendingPlanTier").GetString(), ignoreCase: true);

        var queue = await ReadJsonAsync(await adminClient.GetAsync("/api/admin/payment-requests"));
        var plan = Assert.Single(queue.GetProperty("plans").EnumerateArray());
        Assert.Equal(requestId, plan.GetProperty("requestId").GetGuid());
        Assert.Equal(user.Id, plan.GetProperty("userId").GetGuid());

        var approve = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/plans/{requestId}/approve",
            new { durationDays = 30, reason = "bank transfer received" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var after = await ReadJsonAsync(await userClient.GetAsync("/api/users/me"));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("pendingPlanTier").ValueKind);

        Assert.Empty((await ReadJsonAsync(await adminClient.GetAsync("/api/admin/payment-requests")))
            .GetProperty("plans").EnumerateArray());

        // A decision resolves a request once.
        var second = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/plans/{requestId}/approve", new { durationDays = 30 });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact(DisplayName = "Choosing another paid tier supersedes the earlier pending request; reject closes it")]
    public async Task NewTier_Supersedes_AndReject_Closes()
    {
        var admin = await _factory.SeedUserAsync("plan-admin2", RoleNames.Admin);
        var user = await _factory.SeedUserAsync("plan-user2");
        var userClient = _factory.AuthedClient(user.AccessToken);
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        await userClient.PostAsJsonAsync("/api/users/me/plan", new { tier = "basic" });
        var second = await userClient.PostAsJsonAsync("/api/users/me/plan", new { tier = "elite" });
        var secondId = (await ReadJsonAsync(second)).GetProperty("requestId").GetGuid();

        var queue = await ReadJsonAsync(await adminClient.GetAsync("/api/admin/payment-requests"));
        var only = Assert.Single(queue.GetProperty("plans").EnumerateArray());
        Assert.Equal(secondId, only.GetProperty("requestId").GetGuid());

        var reject = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/plans/{secondId}/reject", new { reason = "no payment" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var me = await ReadJsonAsync(await userClient.GetAsync("/api/users/me"));
        Assert.Equal(JsonValueKind.Null, me.GetProperty("pendingPlanTier").ValueKind);

        var missing = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/plans/{Guid.NewGuid()}/reject", new { });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact(DisplayName = "The free plan applies immediately and is not queued")]
    public async Task FreePlan_IsImmediate()
    {
        var user = await _factory.SeedUserAsync("plan-free");
        var adminUser = await _factory.SeedUserAsync("plan-free-admin", RoleNames.Admin);

        var response = await _factory.AuthedClient(user.AccessToken)
            .PostAsJsonAsync("/api/users/me/plan", new { tier = "free" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var queue = await ReadJsonAsync(await _factory.AuthedClient(adminUser.AccessToken)
            .GetAsync("/api/admin/payment-requests"));
        Assert.Empty(queue.GetProperty("plans").EnumerateArray());
    }

    [Fact(DisplayName = "The payment-request queue is admin only")]
    public async Task Queue_IsAdminOnly()
    {
        var user = await _factory.SeedUserAsync("plan-nonadmin");

        var forbidden = await _factory.AuthedClient(user.AccessToken).GetAsync("/api/admin/payment-requests");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var anonymous = await _factory.AuthedClient().GetAsync("/api/admin/payment-requests");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact(DisplayName = "A featured request is pending for the owner and the admin until payment is confirmed")]
    public async Task FeaturedRequest_ApproveAndReject()
    {
        var admin = await _factory.SeedUserAsync("feat-admin", RoleNames.Admin);
        var owner = await _factory.SeedUserAsync("feat-owner");
        var ownerClient = _factory.AuthedClient(owner.AccessToken);
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        var approvedProperty = await SeedPublishedPropertyAsync(owner.Id);
        var rejectedProperty = await SeedPublishedPropertyAsync(owner.Id);

        var request = await ownerClient.PostAsync($"/api/properties/{approvedProperty}/featured", null);
        Assert.True(request.StatusCode == HttpStatusCode.Accepted, await request.Content.ReadAsStringAsync());
        var approveTx = (await ReadJsonAsync(request)).GetProperty("transactionId").GetGuid();

        var request2 = await ownerClient.PostAsync($"/api/properties/{rejectedProperty}/featured", null);
        var rejectTx = (await ReadJsonAsync(request2)).GetProperty("transactionId").GetGuid();

        var mine = await ReadJsonAsync(await ownerClient.GetAsync("/api/properties/featured/pending"));
        Assert.Equal(2, mine.GetArrayLength());
        Assert.All(mine.EnumerateArray(), row => Assert.NotEqual(Guid.Empty, row.GetProperty("propertyId").GetGuid()));

        var queue = await ReadJsonAsync(await adminClient.GetAsync("/api/admin/payment-requests"));
        Assert.Equal(2, queue.GetProperty("featured").GetArrayLength());

        var approve = await adminClient.PostAsync($"/api/admin/payment-requests/featured/{approveTx}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var reject = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/featured/{rejectTx}/reject", new { reason = "no payment" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        Assert.Equal(0, (await ReadJsonAsync(await ownerClient.GetAsync("/api/properties/featured/pending"))).GetArrayLength());
        Assert.Equal(0, (await ReadJsonAsync(await adminClient.GetAsync("/api/admin/payment-requests")))
            .GetProperty("featured").GetArrayLength());

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var featured = await db.Properties.AsNoTracking().SingleAsync(p => p.Id == approvedProperty);
            var notFeatured = await db.Properties.AsNoTracking().SingleAsync(p => p.Id == rejectedProperty);
            Assert.NotNull(featured.FeaturedUntil);
            Assert.Null(notFeatured.FeaturedUntil);
        });

        var again = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/featured/{rejectTx}/reject", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var missing = await adminClient.PostAsJsonAsync(
            $"/api/admin/payment-requests/featured/{Guid.NewGuid()}/reject", new { });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private Task<Guid> SeedPublishedPropertyAsync(Guid ownerId) =>
        _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = HudhudNestApi.Domain.Listings.Entities.Property.Create(
                $"Featured candidate {Guid.NewGuid():N}",
                "Integration test property",
                ownerId,
                HudhudNestApi.Domain.Enums.ListingType.ForSale);
            db.Properties.Add(property);
            await db.SaveChangesAsync(CancellationToken.None);
            return property.Id;
        });
}
