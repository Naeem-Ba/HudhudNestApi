using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Search.Entities;
using HudhudNestApi.Domain.Search.Enums;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Search;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P3: edit, pause/resume, daily summary, amenity filters and the delivery log.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class SavedSearchAlertsV2Tests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"ss{Guid.NewGuid():N}";
    private Guid _alice;
    private Guid _bob;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _alice = (await _factory.SeedUserAsync($"{_marker}a@test.local")).UserAccountId;
        _bob = (await _factory.SeedUserAsync($"{_marker}b@test.local")).UserAccountId;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "A daily search sends one summary with the count, then waits 24 hours; nothing is lost meanwhile")]
    public async Task Daily_SendsOneSummaryPerDay()
    {
        var searchId = await SeedSearchAsync(SavedSearchFrequency.Daily);
        for (var i = 0; i < 3; i++)
            await PublishAsync($"d{i}", elevator: true);

        var now = DateTime.UtcNow;
        await Sweeper().RunOnceAsync(now, CancellationToken.None);
        Assert.Equal(1, await CountAsync(NotificationType.SavedSearchDigest));
        Assert.Equal(0, await CountAsync(NotificationType.SavedSearchMatch));
        var after = await LoadAsync(searchId);
        Assert.Equal(3, after.NotifiedCount);
        Assert.NotNull(after.LastNotifiedAt);

        await PublishAsync("d3", elevator: true);
        await Sweeper().RunOnceAsync(now.AddHours(1), CancellationToken.None);
        Assert.Equal(1, await CountAsync(NotificationType.SavedSearchDigest));

        await Sweeper().RunOnceAsync(now.AddHours(25), CancellationToken.None);
        Assert.Equal(2, await CountAsync(NotificationType.SavedSearchDigest));
        Assert.Equal(4, (await LoadAsync(searchId)).NotifiedCount);
    }

    [Fact(DisplayName = "A paused search is silent, and resuming does not send what was published while paused")]
    public async Task Pause_And_Resume()
    {
        var searchId = await SeedSearchAsync(SavedSearchFrequency.Instant);
        var alice = await ClientAsync(_alice);
        var bob = await ClientAsync(_bob);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/savedsearch/{searchId}/pause", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync($"/api/savedsearch/{searchId}/pause", null)).StatusCode);

        await PublishAsync("while-paused", elevator: true);
        await Sweeper().RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await CountAsync(NotificationType.SavedSearchMatch));

        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsync($"/api/savedsearch/{searchId}/resume", null)).StatusCode);
        await Sweeper().RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await CountAsync(NotificationType.SavedSearchMatch));

        await PublishAsync("after-resume", elevator: true);
        await Sweeper().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, await CountAsync(NotificationType.SavedSearchMatch));
        Assert.Equal(1, (await LoadAsync(searchId)).NotifiedCount);
    }

    [Fact(DisplayName = "Edit: only the owner can change a search; the amenity filter excludes listings without it; the list shows the new fields")]
    public async Task Edit_And_AmenityFilter()
    {
        var searchId = await SeedSearchAsync(SavedSearchFrequency.Instant);
        var alice = await ClientAsync(_alice);
        var bob = await ClientAsync(_bob);
        var body = new { city = _marker, listingType = "ForRent", hasElevator = true, frequency = "Instant", minRooms = 2 };

        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/savedsearch/{searchId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alice.PutAsJsonAsync($"/api/savedsearch/{searchId}", new { name = "nothing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync($"/api/savedsearch/{searchId}", body)).StatusCode);

        await PublishAsync("no-elevator", elevator: false);
        await PublishAsync("with-elevator", elevator: true);
        await Sweeper().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, await CountAsync(NotificationType.SavedSearchMatch));

        var list = JsonDocument.Parse(await (await alice.GetAsync("/api/savedsearch")).Content.ReadAsStringAsync()).RootElement;
        var mine = Assert.Single(list.EnumerateArray(), s => s.GetProperty("id").GetGuid() == searchId);
        Assert.True(mine.GetProperty("hasElevator").GetBoolean());
        Assert.Equal("Instant", mine.GetProperty("frequency").GetString());
        Assert.False(mine.GetProperty("isPaused").GetBoolean());
        Assert.Equal(1, mine.GetProperty("notifiedCount").GetInt32());
        Assert.Equal(2, mine.GetProperty("minRooms").GetInt32());
    }

    [Fact(DisplayName = "P18-D04: more saved searches than one batch are all processed once, across batches")]
    public async Task Sweep_ProcessesEverySearchAcrossBatches_ExactlyOnce()
    {
        var count = SavedSearchMatchHostedService.SearchBatchSize * 2 + 37;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < count; i++)
            {
                var search = SavedSearch.Create(_alice, $"s{i}", null, _marker, null, null, null, null, null, ListingType.ForRent,
                    null, null, null, null, null, null, null, hasElevator: true, frequency: SavedSearchFrequency.Instant);
                search.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
                db.SavedSearches.Add(search);
            }

            await db.SaveChangesAsync();
        }

        await PublishAsync("batched", elevator: true);

        // The notification service de-duplicates one property per recipient, so the proof of "every search was
        // processed" is each search's own delivery record, not the notification count.
        await Sweeper().RunOnceAsync(CancellationToken.None);
        Assert.Equal(count, await DeliveredSearchesAsync(expectedNotifiedCount: 1));

        // The cursor of every search moved, so a second sweep delivers nothing more to any of them.
        await Sweeper().RunOnceAsync(CancellationToken.None);
        Assert.Equal(count, await DeliveredSearchesAsync(expectedNotifiedCount: 1));
    }

    private async Task<int> DeliveredSearchesAsync(int expectedNotifiedCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SavedSearches.CountAsync(s => s.UserId == _alice && s.NotifiedCount == expectedNotifiedCount && s.LastMatchedAt != null);
    }

    private async Task<Guid> SeedSearchAsync(SavedSearchFrequency frequency)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var search = SavedSearch.Create(_alice, "test", null, _marker, null, null, null, null, null, ListingType.ForRent,
            null, null, null, null, null, null, null, hasElevator: true, frequency: frequency);
        search.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        db.SavedSearches.Add(search);
        await db.SaveChangesAsync();
        return search.Id;
    }

    private async Task PublishAsync(string suffix, bool elevator)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = Property.Create($"{_marker} {suffix}", "وصف كافٍ للإعلان", _bob, ListingType.ForRent, currencyCode: "USD");
        property.City = _marker;
        property.ColdRent = 300m;
        property.Rooms = 3;
        property.HasElevator = elevator;
        db.Properties.Add(property);
        await db.SaveChangesAsync();
    }

    private async Task<SavedSearch> LoadAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SavedSearches.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<int> CountAsync(NotificationType type)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Notifications.CountAsync(n => n.RecipientId == _alice && n.Type == type);
    }

    private SavedSearchMatchHostedService Sweeper() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<SavedSearchMatchHostedService>>());

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
}
