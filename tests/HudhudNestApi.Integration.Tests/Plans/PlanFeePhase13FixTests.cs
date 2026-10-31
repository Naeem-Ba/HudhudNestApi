using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Transactions.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Infrastructure.Plans;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Plans;

/// <summary>
/// QA Phase 13 regression tests over real HTTP and real Postgres: featured fees on listings that can no longer be promoted,
/// the admin queue for extension fees, notices to the user, the paid plan that a "free" tap can no longer drop, the plan
/// state in <c>/api/users/me</c>, stale pending requests, the reason cap and repeated admin actions.
/// </summary>
[Trait("Feature", "PaymentRequests")]
public sealed class PlanFeePhase13FixTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();
    private SeededUser _admin = null!;
    private SeededUser _owner = null!;
    private HttpClient Admin => _factory.AuthedClient(_admin.AccessToken);
    private HttpClient Owner => _factory.AuthedClient(_owner.AccessToken);

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        await _factory.InScopeAsync(services => DatabaseSeeder.SeedAsync(services));
        await RefreshLookupsAsync();
        _admin = await _factory.SeedUserAsync("p13-admin", RoleNames.Admin);
        _owner = await _factory.SeedUserAsync("p13-owner");
    }

    public async Task DisposeAsync()
    {
        await RefreshLookupsAsync();
        _factory.Dispose();
    }

    private Task RefreshLookupsAsync() => _factory.InScopeAsync(
        services => services.GetRequiredService<ICommonLookupService>().RefreshAllAsync());

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    private static async Task<string> Body(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return body;
    }

    private Task<Guid> NewListingAsync(Guid ownerId) => _factory.InScopeAsync(async services =>
    {
        var id = await _factory.SeedPropertyAsync(ownerId);
        var db = services.GetRequiredService<AppDbContext>();
        await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.IsPublished, true)
            .SetProperty(p => p.Status, PropertyStatus.Available));
        return id;
    });

    private Task UpdatePropertyAsync(Guid id, PropertyStatus status) => _factory.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status));
    });

    private Task<List<(NotificationType Type, string Message, Guid? Related)>> NotificationsOfAsync(Guid userId, params NotificationType[] types)
        => _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var rows = await db.Notifications.AsNoTracking()
                .Where(n => n.RecipientId == userId && types.Contains(n.Type))
                .OrderBy(n => n.CreatedAt)
                .Select(n => new { n.Type, n.Message, n.RelatedEntityId })
                .ToListAsync();
            return rows.Select(r => (r.Type, r.Message, r.RelatedEntityId)).ToList();
        });

    private Task<TransactionStatus> FeeStatusAsync(Guid transactionId) => _factory.InScopeAsync(async services =>
        await services.GetRequiredService<AppDbContext>().Transactions.AsNoTracking()
            .Where(t => t.Id == transactionId).Select(t => t.Status).SingleAsync());

    // ── D01: a fee for a listing that can no longer be promoted ──────────────────────────────

    [Theory]
    [InlineData(PropertyStatus.Sold)]
    [InlineData(PropertyStatus.Rented)]
    [InlineData(PropertyStatus.Archived)]
    public async Task FeaturedRequest_OnAListingThatLeftTheMarket_Answers409(PropertyStatus status)
    {
        var id = await NewListingAsync(_owner.Id);
        await UpdatePropertyAsync(id, status);

        await Body(await Owner.PostAsync($"/api/properties/{id}/featured", null), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task FeaturedConfirm_AfterTheListingWasSold_Answers409_AndTheFeeStaysPending()
    {
        var id = await NewListingAsync(_owner.Id);
        var quote = await Json(await Owner.PostAsync($"/api/properties/{id}/featured", null));
        var tx = quote.GetProperty("transactionId").GetGuid();

        await UpdatePropertyAsync(id, PropertyStatus.Sold);

        await Body(await Admin.PostAsync($"/api/admin/payment-requests/featured/{tx}/approve", null), HttpStatusCode.Conflict);
        Assert.Equal(TransactionStatus.Pending, await FeeStatusAsync(tx));

        // The administrator can still close it with the owner.
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/featured/{tx}/reject", new { reason = "sold" }), HttpStatusCode.OK);
        Assert.Equal(TransactionStatus.Failed, await FeeStatusAsync(tx));
    }

    [Fact]
    public async Task FeaturedConfirm_AfterTheOwnerUnpublished_Answers409()
    {
        var id = await NewListingAsync(_owner.Id);
        var tx = (await Json(await Owner.PostAsync($"/api/properties/{id}/featured", null))).GetProperty("transactionId").GetGuid();
        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Properties.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, false)));

        await Body(await Admin.PostAsync($"/api/admin/payment-requests/featured/{tx}/approve", null), HttpStatusCode.Conflict);
        Assert.Equal(TransactionStatus.Pending, await FeeStatusAsync(tx));
    }

    // ── D02 + D03: the extension queue and the owner's notices ───────────────────────────────

    [Fact]
    public async Task ExtensionFee_AppearsInTheQueue_CanBeApproved_AndTheOwnerIsTold()
    {
        var id = await NewListingAsync(_owner.Id);
        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Properties.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAt, DateTime.UtcNow.AddDays(2))));

        var tx = (await Json(await Owner.PostAsync($"/api/properties/{id}/extension", null))).GetProperty("transactionId").GetGuid();

        var queue = await Json(await Admin.GetAsync("/api/admin/payment-requests"));
        var row = Assert.Single(queue.GetProperty("extensions").EnumerateArray());
        Assert.Equal(tx, row.GetProperty("transactionId").GetGuid());
        Assert.Equal(id, row.GetProperty("propertyId").GetGuid());
        Assert.Equal(1m, row.GetProperty("amountUsd").GetDecimal());
        Assert.Empty(queue.GetProperty("featured").EnumerateArray());

        await Body(await Owner.PostAsync($"/api/admin/payment-requests/extensions/{tx}/approve", null), HttpStatusCode.Forbidden);
        var approved = await Json(await Admin.PostAsync($"/api/admin/payment-requests/extensions/{tx}/approve", null));
        Assert.True(approved.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow.AddDays(30));

        Assert.Empty((await Json(await Admin.GetAsync("/api/admin/payment-requests"))).GetProperty("extensions").EnumerateArray());
        await Body(await Admin.PostAsync($"/api/admin/payment-requests/extensions/{tx}/approve", null), HttpStatusCode.Conflict);

        var note = Assert.Single(await NotificationsOfAsync(_owner.Id, NotificationType.ListingFeeConfirmed));
        Assert.Equal(tx, note.Related);
    }

    [Fact]
    public async Task ExtensionFee_CanBeRejected_WithAReason_AndTheOwnerIsTold()
    {
        var id = await NewListingAsync(_owner.Id);
        await _factory.InScopeAsync(async services =>
            await services.GetRequiredService<AppDbContext>().Properties.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAt, DateTime.UtcNow.AddDays(2))));
        var tx = (await Json(await Owner.PostAsync($"/api/properties/{id}/extension", null))).GetProperty("transactionId").GetGuid();

        await Body(await Owner.PostAsJsonAsync($"/api/admin/payment-requests/extensions/{tx}/reject", new { reason = "x" }), HttpStatusCode.Forbidden);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/extensions/{tx}/reject", new { reason = "الحوالة لم تصل" }), HttpStatusCode.OK);

        Assert.Equal(TransactionStatus.Failed, await FeeStatusAsync(tx));
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/extensions/{tx}/reject", new { }), HttpStatusCode.Conflict);
        await Body(await Admin.PostAsync($"/api/admin/payment-requests/extensions/{tx}/approve", null), HttpStatusCode.Conflict);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/extensions/{Guid.NewGuid()}/reject", new { }), HttpStatusCode.NotFound);

        var note = Assert.Single(await NotificationsOfAsync(_owner.Id, NotificationType.ListingFeeRejected));
        Assert.Contains("الحوالة لم تصل", note.Message);
    }

    [Fact]
    public async Task RejectingAFeaturedFee_AsAFeaturedFee_NotAnExtension()
    {
        var id = await NewListingAsync(_owner.Id);
        var tx = (await Json(await Owner.PostAsync($"/api/properties/{id}/featured", null))).GetProperty("transactionId").GetGuid();

        // The wrong-kind endpoint refuses and leaves the fee alone.
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/extensions/{tx}/reject", new { }), HttpStatusCode.Conflict);
        Assert.Equal(TransactionStatus.Pending, await FeeStatusAsync(tx));
    }

    // ── D03: plan notices, plan state on /users/me; D04: the paid plan survives a "free" tap ─

    [Fact]
    public async Task PaidPlan_IsNotDroppedByChoosingFree_AndMeShowsTheState()
    {
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/activate",
            new { tier = "basic", durationDays = 30 }), HttpStatusCode.OK);

        var me = await Json(await Owner.GetAsync("/api/users/me"));
        Assert.Equal("Active", me.GetProperty("planStatus").GetString());
        Assert.True(me.GetProperty("planExpiresAt").GetDateTime() > DateTime.UtcNow.AddDays(29));

        var refused = await Body(await Owner.PostAsJsonAsync("/api/users/me/plan", new { tier = "free" }), HttpStatusCode.Conflict);
        Assert.Contains("PAID_PLAN_ACTIVE", refused);
        Assert.Equal("basic", (await Json(await Owner.GetAsync("/api/users/me"))).GetProperty("planTier").GetString(), ignoreCase: true);

        // Once the administrator ends it, the free tier is open again and /me says the benefits are gone.
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/cancel", new { }), HttpStatusCode.OK);
        Assert.Equal("Cancelled", (await Json(await Owner.GetAsync("/api/users/me"))).GetProperty("planStatus").GetString());
        await Body(await Owner.PostAsJsonAsync("/api/users/me/plan", new { tier = "free" }), HttpStatusCode.NoContent);
        Assert.Equal("Active", (await Json(await Owner.GetAsync("/api/users/me"))).GetProperty("planStatus").GetString());
    }

    [Fact]
    public async Task PlanRequest_ApproveAndReject_NotifyTheUser()
    {
        var other = await _factory.SeedUserAsync("p13-other");

        var first = await Json(await Owner.PostAsJsonAsync("/api/users/me/plan", new { tier = "premium" }));
        var approveId = first.GetProperty("requestId").GetGuid();
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/plans/{approveId}/approve", new { durationDays = 30 }), HttpStatusCode.OK);
        var approved = Assert.Single(await NotificationsOfAsync(_owner.Id, NotificationType.PlanRequestApproved));
        Assert.Equal(approveId, approved.Related);

        var second = await Json(await _factory.AuthedClient(other.AccessToken).PostAsJsonAsync("/api/users/me/plan", new { tier = "basic" }));
        var rejectId = second.GetProperty("requestId").GetGuid();
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/plans/{rejectId}/reject", new { reason = "لم يصل المبلغ" }), HttpStatusCode.OK);
        var rejected = Assert.Single(await NotificationsOfAsync(other.Id, NotificationType.PlanRequestRejected));
        Assert.Contains("لم يصل المبلغ", rejected.Message);
        Assert.Empty(await NotificationsOfAsync(_owner.Id, NotificationType.PlanRequestRejected));
    }

    // ── D06: a direct grant closes the user's pending request ────────────────────────────────

    [Fact]
    public async Task DirectGrant_ClosesThePendingRequest_SoItCannotOverwriteItLater()
    {
        var pending = await Json(await Owner.PostAsJsonAsync("/api/users/me/plan", new { tier = "premium" }));
        var requestId = pending.GetProperty("requestId").GetGuid();

        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/activate",
            new { tier = "basic", durationDays = 30 }), HttpStatusCode.OK);

        Assert.Empty((await Json(await Admin.GetAsync("/api/admin/payment-requests"))).GetProperty("plans").EnumerateArray());
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/plans/{requestId}/approve", new { durationDays = 30 }), HttpStatusCode.Conflict);
        Assert.Equal(JsonValueKind.Null, (await Json(await Owner.GetAsync("/api/users/me"))).GetProperty("pendingPlanTier").ValueKind);
    }

    // ── D03: the expiry sweep ────────────────────────────────────────────────────────────────

    private Task SweepAsync() => _factory.InScopeAsync(async services =>
    {
        var service = new PlanExpiryNoticeHostedService(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PlanExpiryNoticeHostedService>.Instance);
        await service.RunOnceAsync(CancellationToken.None);
    });

    private Task SetPlanEndAsync(Guid userId, DateTime end) => _factory.InScopeAsync(async services =>
        await services.GetRequiredService<AppDbContext>().UserAccounts.Where(a => a.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PlanExpiresAt, end)));

    [Fact]
    public async Task ExpirySweep_WarnsOnce_ThenAnnouncesTheEndOnce_AndIgnoresOldLapses()
    {
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/activate",
            new { tier = "basic", durationDays = 30 }), HttpStatusCode.OK);

        await SweepAsync();
        Assert.Empty(await NotificationsOfAsync(_owner.Id, NotificationType.PlanExpiringSoon, NotificationType.PlanExpired));

        await SetPlanEndAsync(_owner.Id, DateTime.UtcNow.AddDays(2));
        await SweepAsync();
        await SweepAsync();
        Assert.Single(await NotificationsOfAsync(_owner.Id, NotificationType.PlanExpiringSoon));

        await SetPlanEndAsync(_owner.Id, DateTime.UtcNow.AddHours(-3));
        await SweepAsync();
        await SweepAsync();
        Assert.Single(await NotificationsOfAsync(_owner.Id, NotificationType.PlanExpired));

        // A plan that lapsed weeks ago is not announced by the first sweep after deployment.
        var old = await _factory.SeedUserAsync("p13-old");
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{old.Id}/subscription/activate",
            new { tier = "basic", durationDays = 30 }), HttpStatusCode.OK);
        await SetPlanEndAsync(old.Id, DateTime.UtcNow.AddDays(-30));
        await SweepAsync();
        Assert.Empty(await NotificationsOfAsync(old.Id, NotificationType.PlanExpiringSoon, NotificationType.PlanExpired));
    }

    // ── D07 + D08 ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(501, false)]
    [InlineData(500, true)]
    public async Task AdminReason_IsCappedAt500Characters(int length, bool accepted)
    {
        var response = await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/activate",
            new { tier = "basic", durationDays = 5, reason = new string('ن', length) });
        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminReason_WithANulCharacter_Answers400()
    {
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/activate",
            new { tier = "basic", durationDays = 5, reason = "a\u0000b" }), HttpStatusCode.BadRequest);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/payment-requests/featured/{Guid.NewGuid()}/reject",
            new { reason = new string('x', 5000) }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RepeatedAdminActions_AnswerConflict_NotASecondSuccess()
    {
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/activate",
            new { tier = "basic", durationDays = 30 }), HttpStatusCode.OK);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/cancel", new { }), HttpStatusCode.OK);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/users/{_owner.Id}/subscription/cancel", new { }), HttpStatusCode.Conflict);

        var id = await NewListingAsync(_owner.Id);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/properties/{id}/unfeature", new { }), HttpStatusCode.Conflict);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/properties/{id}/feature", new { days = 5 }), HttpStatusCode.OK);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/properties/{id}/unfeature", new { }), HttpStatusCode.OK);
        await Body(await Admin.PostAsJsonAsync($"/api/admin/properties/{id}/unfeature", new { }), HttpStatusCode.Conflict);
    }
}
