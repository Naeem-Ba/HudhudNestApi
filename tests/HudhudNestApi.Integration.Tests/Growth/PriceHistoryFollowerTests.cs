using System.Net;
using System.Net.Http.Json;
using HudhudNestApi.Application.Listings.Queries.GetPropertyPriceHistory;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Listings;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P1: public price timeline and price-change alerts for users who saved a listing.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class PriceHistoryFollowerTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"ph{Guid.NewGuid():N}";
    private Guid _owner;
    private Guid _follower;
    private Guid _bystander;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _owner = (await _factory.SeedUserAsync($"{_marker}o@test.local")).UserAccountId;
        _follower = (await _factory.SeedUserAsync($"{_marker}f@test.local")).UserAccountId;
        _bystander = (await _factory.SeedUserAsync($"{_marker}b@test.local")).UserAccountId;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> SeedListingAsync(bool published, params (decimal? Old, decimal? New, string Currency, string? OldCurrency, int MinutesAgo)[] changes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = Property.Create($"{_marker} flat", "وصف كافٍ للإعلان", _owner, ListingType.ForSale, currencyCode: "USD", isPublished: published);
        property.PurchasePrice = changes.Length > 0 ? changes[^1].New : 100_000m;
        db.Properties.Add(property);
        db.Favorites.Add(new Favorite { UserId = _follower, PropertyId = property.Id });
        db.Favorites.Add(new Favorite { UserId = _owner, PropertyId = property.Id });

        foreach (var change in changes)
        {
            var row = PropertyPriceHistory.Create(property.Id, "PurchasePrice", change.Old, change.New, change.Currency, _owner, change.OldCurrency);
            db.PropertyPriceHistories.Add(row);
            db.Entry(row).Property(nameof(PropertyPriceHistory.ChangedAt)).CurrentValue = DateTime.UtcNow.AddMinutes(-change.MinutesAgo);
        }

        await db.SaveChangesAsync();
        return property.Id;
    }

    private PriceChangeFollowerNotificationHostedService Sweeper() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<PriceChangeFollowerNotificationHostedService>>());

    private async Task<List<(Guid Recipient, string Message)>> AlertsAsync(Guid propertyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Notifications
                .Where(n => n.PropertyId == propertyId && n.Type == NotificationType.FavoritePriceChanged)
                .Select(n => new { n.RecipientId, n.Message })
                .ToListAsync())
            .Select(n => (n.RecipientId, n.Message))
            .ToList();
    }

    [Fact(DisplayName = "Only the follower is told, once, with the net change of several settled edits")]
    public async Task Follower_GetsOneNetAlert_OwnerAndBystanderDoNot()
    {
        var id = await SeedListingAsync(true, (100_000m, 95_000m, "USD", null, 30), (95_000m, 90_000m, "USD", null, 20));

        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);
        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);

        var alert = Assert.Single(await AlertsAsync(id));
        Assert.Equal(_follower, alert.Recipient);
        Assert.Contains("100000 USD", alert.Message);
        Assert.Contains("90000 USD", alert.Message);
        Assert.DoesNotContain("95000", alert.Message);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.PropertyPriceHistories.CountAsync(h => h.PropertyId == id && h.FollowersNotifiedAt == null));
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.RecipientId == _bystander));
    }

    [Fact(DisplayName = "A change that was undone again announces nothing; a fresh change waits until it settles")]
    public async Task UndoneChange_IsSilent_FreshChangeWaits()
    {
        var undone = await SeedListingAsync(true, (100_000m, 80_000m, "USD", null, 30), (80_000m, 100_000m, "USD", null, 25));
        var fresh = await SeedListingAsync(true, (100_000m, 90_000m, "USD", null, 1));

        await Sweeper().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Empty(await AlertsAsync(undone));
        Assert.Empty(await AlertsAsync(fresh));

        await Sweeper().RunOnceAsync(DateTime.UtcNow.AddMinutes(10), CancellationToken.None);
        Assert.Single(await AlertsAsync(fresh));
    }

    [Fact(DisplayName = "Price history is public for a live listing, hides who changed it, and is 404 for a draft")]
    public async Task Endpoint_IsPublicForLiveListings_Only()
    {
        var live = await SeedListingAsync(true, (100_000m, 90_000m, "USD", null, 30), (90_000m, 90_000m, "EUR", "USD", 10));
        var draft = await SeedListingAsync(false);
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/properties/{live}/price-history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_owner.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("changedBy", body, StringComparison.OrdinalIgnoreCase);

        var dto = await response.Content.ReadFromJsonAsync<PropertyPriceHistoryDto>();
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Changes.Count);
        Assert.Equal("EUR", dto.Changes[0].CurrencyCode);
        Assert.Equal("USD", dto.Changes[0].OldCurrencyCode);
        Assert.Null(dto.Changes[0].ChangePercent);
        Assert.Equal(-10.0m, dto.Changes[1].ChangePercent);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/properties/{draft}/price-history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/properties/{Guid.NewGuid()}/price-history")).StatusCode);
    }
}
