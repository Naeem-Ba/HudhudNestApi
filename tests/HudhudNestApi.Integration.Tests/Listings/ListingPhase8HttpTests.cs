using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Bookings.Entities;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Listings;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Messaging;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// QA Phase 8 fixes over real HTTP and PostgreSQL: administrative take-down (P8-D10), the message sending
/// limit (P8-D07), open visits closed when a listing is deleted by its owner or by the expiry sweep (P8-D08),
/// and the extension/publish rules (P8-D02, P8-D03).
/// </summary>
[Trait("Feature", "Listings")]
public sealed class ListingPhase8HttpTests : IClassFixture<MessagingApiTestFactory>, IAsyncLifetime
{
    private readonly MessagingApiTestFactory _factory;

    public ListingPhase8HttpTests(MessagingApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ── P8-D10: take-down ───────────────────────────────────────────────

    [Fact(DisplayName = "P8-D10: an admin takes a listing down, the public loses it, the owner sees the reason and cannot bring it back; restoring reverses it")]
    public async Task TakeDown_Hides_BlocksOwner_AndRestoreReverses()
    {
        var admin = await _factory.SeedUserAsync("admin", "Admin");
        var owner = await _factory.SeedUserAsync("owner");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id, "إعلان مخالف");
        var anonymous = _factory.CreateClient();
        var adminClient = _factory.AuthedClient(admin.AccessToken);
        var ownerClient = _factory.AuthedClient(owner.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/Properties/{propertyId}")).StatusCode);

        // Only an admin may do it; a reason is mandatory.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ownerClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/take-down", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await adminClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/take-down", new { reason = "  " })).StatusCode);

        var takeDown = await adminClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/take-down", new { reason = "صور لا تخص العقار" });
        Assert.Equal(HttpStatusCode.OK, takeDown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await adminClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/take-down", new { reason = "again" })).StatusCode);

        // The public no longer sees it; the owner still does, with the reason.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/Properties/{propertyId}")).StatusCode);
        var manage = await ReadJson(await ownerClient.GetAsync($"/api/Properties/{propertyId}/manage"));
        Assert.True(manage.GetProperty("isTakenDown").GetBoolean());
        Assert.Equal("صور لا تخص العقار", manage.GetProperty("takenDownReason").GetString());

        // The owner can neither republish nor extend it.
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PostAsync($"/api/Properties/{propertyId}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PostAsync($"/api/Properties/{propertyId}/extension", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/Properties/{propertyId}")).StatusCode);

        // Audited, and the owner was told.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ListingTakenDownByAdmin && a.UserId == admin.Id));
            var note = await db.Notifications.SingleAsync(n => n.RecipientId == owner.Id && n.Type == NotificationType.ListingTakenDown);
            Assert.Contains("صور لا تخص العقار", note.Message);
            Assert.Equal(propertyId, note.PropertyId);
        });

        // Restore.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ownerClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/restore", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await adminClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/restore", new { reason = "تم التصحيح" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await adminClient.PostAsJsonAsync($"/api/Admin/properties/{propertyId}/restore", new { reason = "twice" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/Properties/{propertyId}")).StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ListingRestoredByAdmin));
            Assert.True(await db.Notifications.AnyAsync(n => n.RecipientId == owner.Id && n.Type == NotificationType.ListingRestored));
        });
    }

    [Fact(DisplayName = "P8-D10: the expiry sweep does not expire or warn about a taken-down listing")]
    public async Task TakeDown_IsLeftAloneByTheExpirySweep()
    {
        var admin = await _factory.SeedUserAsync("admin", "Admin");
        var owner = await _factory.SeedUserAsync("owner");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);
        await SetExpiresAtAsync(propertyId, DateTime.UtcNow.AddDays(-1));

        await _factory.AuthedClient(admin.AccessToken)
            .PostAsJsonAsync($"/api/Admin/properties/{propertyId}/take-down", new { reason = "hold" });

        await SweepAsync();

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            Assert.Equal(PropertyStatus.Available, property.Status);
            Assert.True(property.IsTakenDown);
            Assert.False(await db.Notifications.AnyAsync(n => n.RecipientId == owner.Id && n.Type == NotificationType.ListingExpired));
        });
    }

    // ── P8-D02 / P8-D03: extension and publish ───────────────────────────

    [Fact(DisplayName = "P8-D02: no extension for a sold listing (409); P8-D03: an expired listing cannot be published back for free (409)")]
    public async Task SoldCannotBeExtended_ExpiredCannotBePublishedWithoutPaying()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var client = _factory.AuthedClient(owner.AccessToken);

        var sold = await _factory.SeedPropertyAsync(owner.Id, "مباع");
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = await db.Properties.SingleAsync(p => p.Id == sold);
            property.ExpiresAt = DateTime.UtcNow.AddDays(2);
            property.ChangeStatus(PropertyStatus.Sold);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/Properties/{sold}/extension", null)).StatusCode);

        var expired = await _factory.SeedPropertyAsync(owner.Id, "منتهٍ");
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var property = await db.Properties.SingleAsync(p => p.Id == expired);
            property.ExpiresAt = DateTime.UtcNow.AddDays(-2);
            property.MarkExpired();
            db.PropertyImages.Add(new HudhudNestApi.Domain.Listings.Entities.PropertyImage
            {
                PropertyId = expired,
                Url = "https://cdn.test/1.jpg",
                PublicId = "p-expired",
                IsMain = true
            });
            await db.SaveChangesAsync();
        });
        var publish = await client.PostAsync($"/api/Properties/{expired}/publish", null);
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var property = await services.GetRequiredService<AppDbContext>().Properties.SingleAsync(p => p.Id == expired);
            Assert.Equal(PropertyStatus.Expired, property.Status);
            Assert.False(property.IsPublished);
            Assert.True(property.ExpiresAt < DateTime.UtcNow);
        });
    }

    // ── P8-D07: sending limit ─────────────────────────────────────────────

    [Fact(DisplayName = "P8-D07: the 11th message about one listing within ten minutes is a 429 with Retry-After; another listing is unaffected")]
    public async Task Messages_AreLimitedPerListing()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var visitor = await _factory.SeedUserAsync("visitor");
        var listingA = await _factory.SeedPropertyAsync(owner.Id, "أ");
        var listingB = await _factory.SeedPropertyAsync(owner.Id, "ب");
        var client = _factory.AuthedClient(visitor.AccessToken);

        for (var i = 1; i <= 10; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/Messages", new { propertyId = listingA, receiverId = (Guid?)null, content = $"رسالة {i}" });
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/Messages", new { propertyId = listingA, receiverId = (Guid?)null, content = "رسالة 11" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter is not null);
        var body = await ReadJson(blocked);
        Assert.Equal("MESSAGE_RATE_LIMIT", body.GetProperty("code").GetString());

        var other = await client.PostAsJsonAsync("/api/Messages", new { propertyId = listingB, receiverId = (Guid?)null, content = "سؤال آخر" });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(10, await db.Messages.CountAsync(m => m.PropertyId == listingA && m.SenderId == visitor.Id));
            // Ten messages in a burst raised one notification, not ten.
            Assert.Equal(1, await db.Notifications.CountAsync(n =>
                n.RecipientId == owner.Id && n.Type == NotificationType.NewMessage && n.PropertyId == listingA));
        });
    }

    // ── P8-D01: input the database cannot store ───────────────────────────

    [Fact(DisplayName = "P8-D01: a NUL character in a message is a 422, not a 500")]
    public async Task Message_WithANulCharacter_IsRefusedAs422()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var visitor = await _factory.SeedUserAsync("visitor");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id);

        var response = await _factory.AuthedClient(visitor.AccessToken)
            .PostAsJsonAsync("/api/Messages", new { propertyId, receiverId = (Guid?)null, content = "hello\u0000world" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── P8-D08: visits closed with the listing ─────────────────────────────

    [Fact(DisplayName = "P8-D08: deleting a listing cancels its open visits and tells the visitor")]
    public async Task OwnerDelete_CancelsOpenVisits_AndNotifiesTheVisitor()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var visitor = await _factory.SeedUserAsync("visitor");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id, "للحذف");
        var (pending, done) = await SeedVisitsAsync(propertyId, visitor.Id);

        var delete = await _factory.AuthedClient(owner.AccessToken).DeleteAsync($"/api/Properties/{propertyId}");
        Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(VisitStatus.Cancelled, (await db.VisitRequests.SingleAsync(v => v.Id == pending)).Status);
            Assert.Equal(VisitStatus.Completed, (await db.VisitRequests.SingleAsync(v => v.Id == done)).Status);
            var note = await db.Notifications.SingleAsync(n =>
                n.RecipientId == visitor.Id && n.Type == NotificationType.VisitCancelledByHost);
            Assert.Equal(pending, note.RelatedEntityId);
        });
    }

    [Fact(DisplayName = "P8-D08: the expiry sweep that deletes an expired listing after its grace period also cancels its open visits")]
    public async Task SweepDelete_CancelsOpenVisits()
    {
        var owner = await _factory.SeedUserAsync("owner");
        var visitor = await _factory.SeedUserAsync("visitor");
        var propertyId = await _factory.SeedPropertyAsync(owner.Id, "منتهٍ منذ زمن");
        var (pending, _) = await SeedVisitsAsync(propertyId, visitor.Id);
        await SetExpiresAtAsync(propertyId, DateTime.UtcNow.AddDays(-60));
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.Properties.SingleAsync(p => p.Id == propertyId)).MarkExpired();
            await db.SaveChangesAsync();
        });

        await SweepAsync();

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.False(await db.Properties.AnyAsync(p => p.Id == propertyId));
            Assert.Equal(VisitStatus.Cancelled, (await db.VisitRequests.SingleAsync(v => v.Id == pending)).Status);
            Assert.True(await db.Notifications.AnyAsync(n =>
                n.RecipientId == visitor.Id && n.Type == NotificationType.VisitCancelledByHost));
        });
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<(Guid Pending, Guid Completed)> SeedVisitsAsync(Guid propertyId, Guid visitorId)
    {
        var pending = VisitRequest.Create(propertyId, visitorId, DateTime.UtcNow.AddDays(3), "Sam", "+49 170 1234567");
        var completed = VisitRequest.Create(propertyId, visitorId, DateTime.UtcNow.AddDays(4), "Sam", "+49 170 1234567");
        completed.Confirm();
        completed.Complete();

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            db.VisitRequests.AddRange(pending, completed);
            await db.SaveChangesAsync();
        });
        return (pending.Id, completed.Id);
    }

    private Task SetExpiresAtAsync(Guid propertyId, DateTime expiresAt)
        => _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            (await db.Properties.SingleAsync(p => p.Id == propertyId)).ExpiresAt = expiresAt;
            await db.SaveChangesAsync();
        });

    private async Task SweepAsync()
    {
        var service = new ListingExpiryHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<ListingExpiryHostedService>>());
        await service.RunOnceAsync(CancellationToken.None);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
