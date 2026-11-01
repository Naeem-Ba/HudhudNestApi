using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Demand;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>
/// Growth engine P2, the brief's end-to-end path: a buyer posts a request → an advertiser publishes a fitting
/// listing → the matcher tells the buyer → the buyer opens and saves it. Plus the privacy and ownership rules.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class SearchRequestFlowTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"sr{Guid.NewGuid():N}";
    private Guid _buyer;
    private Guid _advertiser;
    private Guid _otherAdvertiser;
    private Guid _stranger;
    private int _governorateId;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _buyer = (await _factory.SeedUserAsync($"{_marker}b@test.local")).UserAccountId;
        _advertiser = (await _factory.SeedUserAsync($"{_marker}a@test.local")).UserAccountId;
        _otherAdvertiser = (await _factory.SeedUserAsync($"{_marker}o@test.local")).UserAccountId;
        _stranger = (await _factory.SeedUserAsync($"{_marker}s@test.local")).UserAccountId;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Governorates.OrderBy(g => g.Id).FirstOrDefaultAsync();
        if (existing is null)
        {
            existing = Governorate.Create("محافظة اختبار", "Test Governorate");
            db.Governorates.Add(existing);
            await db.SaveChangesAsync();
        }
        _governorateId = existing.Id;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<HttpClient> ClientAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("user");
        var roles = await users.GetRolesAsync(user);
        var token = tokens.GenerateAccessToken(new AccessTokenSubject(user.Id, user.Email, user.UserName, user.SecurityStamp), roles.ToArray());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> PublishListingAsync(Guid ownerId, decimal rent, int rooms, bool balcony)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = Property.Create($"{_marker} flat {rent}", "وصف كافٍ للإعلان", ownerId, ListingType.ForRent, currencyCode: "USD");
        property.GovernorateId = _governorateId;
        property.ColdRent = rent;
        property.Rooms = rooms;
        property.HasBalcony = balcony;
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property.Id;
    }

    private object RequestBody(string? details = null) => new
    {
        listingType = "ForRent",
        governorateId = _governorateId,
        maxBudget = 400m,
        currencyCode = "USD",
        minRooms = 2,
        needsBalcony = true,
        needsElevator = false,
        needsParking = false,
        needsFurnished = false,
        details,
    };

    private SearchRequestMatchHostedService Sweeper() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<SearchRequestMatchHostedService>>());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<int> NotificationsAsync(Guid userId, NotificationType type)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Notifications.CountAsync(n => n.RecipientId == userId && n.Type == type);
    }

    [Fact(DisplayName = "Request → fitting listing published → buyer notified once → opens and saves it; advertiser learns of demand")]
    public async Task EndToEnd_BuyerIsMatchedAndNotified()
    {
        var buyer = await ClientAsync(_buyer);
        var created = await buyer.PostAsJsonAsync("/api/search-requests", RequestBody("قرب جامعة دمشق"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var requestId = (await JsonOf(created)).GetProperty("id").GetGuid();

        // First sweep evaluates the (empty) market silently and moves the cursor.
        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);

        var fitting = await PublishListingAsync(_advertiser, rent: 350m, rooms: 3, balcony: true);
        var tooExpensive = await PublishListingAsync(_advertiser, rent: 900m, rooms: 3, balcony: true);
        var tooSmall = await PublishListingAsync(_advertiser, rent: 300m, rooms: 1, balcony: true);

        await Sweeper().RunOnceAsync(DateTime.UtcNow.AddMinutes(16), CancellationToken.None);
        await Sweeper().RunOnceAsync(DateTime.UtcNow.AddMinutes(32), CancellationToken.None);

        Assert.Equal(1, await NotificationsAsync(_buyer, NotificationType.SearchRequestMatch));
        Assert.Equal(1, await NotificationsAsync(_advertiser, NotificationType.ListingDemandMatch));

        var matches = await JsonOf(await buyer.GetAsync($"/api/search-requests/{requestId}/matches"));
        var only = Assert.Single(matches.EnumerateArray());
        Assert.Equal(fitting, only.GetProperty("propertyId").GetGuid());
        Assert.True(only.GetProperty("score").GetInt32() >= 50);
        Assert.Contains("balcony_yes", only.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));
        Assert.DoesNotContain(matches.EnumerateArray(), m => m.GetProperty("propertyId").GetGuid() == tooExpensive || m.GetProperty("propertyId").GetGuid() == tooSmall);

        // The buyer opens the listing (public) and saves it.
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync($"/api/properties/{fitting}")).StatusCode);
        var favorite = await buyer.PostAsync($"/api/favorites/{fitting}", null);
        Assert.True(favorite.IsSuccessStatusCode, $"favorite: {(int)favorite.StatusCode}");

        // The advertiser sees the anonymous demand on their own listing, with no trace of the buyer.
        var advertiser = await ClientAsync(_advertiser);
        var demandResponse = await advertiser.GetAsync($"/api/search-requests/for-listing/{fitting}");
        Assert.Equal(HttpStatusCode.OK, demandResponse.StatusCode);
        var demandText = await demandResponse.Content.ReadAsStringAsync();
        Assert.Contains(requestId.ToString(), demandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_buyer.ToString(), demandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@test.local", demandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Board: advertisers only, anonymous; a proposal notifies the requester once; strangers cannot read or propose for others")]
    public async Task Board_And_Proposals_RespectPrivacyAndOwnership()
    {
        var buyer = await ClientAsync(_buyer);
        var requestId = (await JsonOf(await buyer.PostAsJsonAsync("/api/search-requests", RequestBody()))).GetProperty("id").GetGuid();

        var stranger = await ClientAsync(_stranger);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/api/search-requests/board")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/search-requests/{requestId}/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/search-requests/{requestId}/pause", null)).StatusCode);

        var listing = await PublishListingAsync(_otherAdvertiser, rent: 380m, rooms: 2, balcony: false);
        var other = await ClientAsync(_otherAdvertiser);
        var board = await other.GetAsync("/api/search-requests/board");
        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
        var boardText = await board.Content.ReadAsStringAsync();
        Assert.Contains(requestId.ToString(), boardText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_buyer.ToString(), boardText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", boardText, StringComparison.OrdinalIgnoreCase);

        // Someone else's listing cannot be proposed.
        var strangerPropose = await stranger.PostAsJsonAsync($"/api/search-requests/{requestId}/proposals", new { propertyId = listing });
        Assert.True(strangerPropose.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"got {(int)strangerPropose.StatusCode}");

        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync($"/api/search-requests/{requestId}/proposals", new { propertyId = listing })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync($"/api/search-requests/{requestId}/proposals", new { propertyId = listing })).StatusCode);
        Assert.Equal(1, await NotificationsAsync(_buyer, NotificationType.SearchRequestProposal));

        var matches = await JsonOf(await buyer.GetAsync($"/api/search-requests/{requestId}/matches"));
        Assert.Equal("Proposed", Assert.Single(matches.EnumerateArray()).GetProperty("source").GetString());

        // A listing outside the budget cannot be pushed onto the request.
        var expensive = await PublishListingAsync(_otherAdvertiser, rent: 2000m, rooms: 3, balcony: true);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync($"/api/search-requests/{requestId}/proposals", new { propertyId = expensive })).StatusCode);
    }

    [Fact(DisplayName = "Lifecycle: contact details are refused, pause hides it from the board, close is final, five open requests at most")]
    public async Task Lifecycle_And_Limits()
    {
        var buyer = await ClientAsync(_buyer);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await buyer.PostAsJsonAsync("/api/search-requests", RequestBody("اتصل على 0944 123 456"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await buyer.PostAsJsonAsync("/api/search-requests", RequestBody("me@example.com"))).StatusCode);

        var id = (await JsonOf(await buyer.PostAsJsonAsync("/api/search-requests", RequestBody()))).GetProperty("id").GetGuid();
        Assert.Equal("Paused", (await JsonOf(await buyer.PostAsync($"/api/search-requests/{id}/pause", null))).GetProperty("status").GetString());

        await PublishListingAsync(_otherAdvertiser, rent: 100m, rooms: 2, balcony: true);
        var other = await ClientAsync(_otherAdvertiser);
        Assert.DoesNotContain(id.ToString(), await (await other.GetAsync("/api/search-requests/board")).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Active", (await JsonOf(await buyer.PostAsync($"/api/search-requests/{id}/resume", null))).GetProperty("status").GetString());
        Assert.Equal("Closed", (await JsonOf(await buyer.PostAsync($"/api/search-requests/{id}/close", null))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.PostAsync($"/api/search-requests/{id}/resume", null)).StatusCode);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created, (await buyer.PostAsJsonAsync("/api/search-requests", RequestBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.PostAsJsonAsync("/api/search-requests", RequestBody())).StatusCode);
    }
}
