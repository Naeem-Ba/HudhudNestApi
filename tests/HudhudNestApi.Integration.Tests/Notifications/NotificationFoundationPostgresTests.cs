using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Notifications.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Search.Entities;
using HudhudNestApi.Infrastructure.Notifications;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Search;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Notifications;

/// <summary>Notification dedupe, retention and saved-search matching against a real Postgres.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Notifications")]
public sealed class NotificationFoundationPostgresTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"nf{Guid.NewGuid():N}";
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

    private static Notification Make(Guid recipient, string? key, bool read = false, DateTime? readAt = null, bool deleted = false, DateTime? deletedAt = null) =>
        new()
        {
            RecipientId = recipient,
            Type = NotificationType.UserRated,
            Message = "x",
            DedupeKey = key,
            IsRead = read,
            ReadAt = readAt,
            IsDeleted = deleted,
            DeletedAt = deletedAt,
        };

    [Fact(DisplayName = "The same dedupe key for the same recipient is stored once; other recipients and keyless rows are unaffected")]
    public async Task Repository_IgnoresADuplicateKey()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await repo.AddAsync(Make(_alice, "k1")));
        Assert.False(await repo.AddAsync(Make(_alice, "k1")));
        Assert.True(await repo.AddAsync(Make(_bob, "k1")));
        Assert.True(await repo.AddAsync(Make(_alice, null)));
        Assert.True(await repo.AddAsync(Make(_alice, null)));

        Assert.Equal(3, await db.Notifications.CountAsync(n => n.RecipientId == _alice));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.RecipientId == _bob));
    }

    [Fact(DisplayName = "A removed notification still blocks its key, so a retry cannot resurrect it")]
    public async Task SoftDeletedRow_StillBlocksTheKey()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        Assert.True(await repo.AddAsync(Make(_alice, "k2", deleted: true, deletedAt: DateTime.UtcNow)));
        Assert.False(await repo.AddAsync(Make(_alice, "k2")));
    }

    [Fact(DisplayName = "Retention removes old read and old removed notifications, never unread ones or recent ones")]
    public async Task Retention_RemovesOnlyWhatIsPastItsWindow()
    {
        var now = DateTime.UtcNow;
        Guid oldRead, oldDeleted, oldUnread, recentRead, recentDeleted;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = new[]
            {
                Make(_alice, null, read: true, readAt: now.AddDays(-200)),
                Make(_alice, null, deleted: true, deletedAt: now.AddDays(-40)),
                Make(_alice, null),
                Make(_alice, null, read: true, readAt: now.AddDays(-10)),
                Make(_alice, null, deleted: true, deletedAt: now.AddDays(-2)),
            };
            rows[2].CreatedAt = now.AddDays(-400);
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
            (oldRead, oldDeleted, oldUnread, recentRead, recentDeleted) = (rows[0].Id, rows[1].Id, rows[2].Id, rows[3].Id, rows[4].Id);
        }

        var service = new NotificationRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            _factory.Services.GetRequiredService<ILogger<NotificationRetentionHostedService>>());

        var removed = await service.RunOnceAsync(CancellationToken.None, now);

        Assert.Equal(2, removed);
        using var check = _factory.Services.CreateScope();
        var verify = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var left = await verify.Notifications.IgnoreQueryFilters().Select(n => n.Id).ToListAsync();
        Assert.DoesNotContain(oldRead, left);
        Assert.DoesNotContain(oldDeleted, left);
        Assert.Contains(oldUnread, left);
        Assert.Contains(recentRead, left);
        Assert.Contains(recentDeleted, left);
    }

    [Fact(DisplayName = "A saved search with more matches than the per-run cap notifies all of them across sweeps, each once")]
    public async Task SavedSearch_DoesNotDropMatchesBeyondTheCap_AndDoesNotRepeat()
    {
        var start = DateTime.UtcNow.AddHours(-3);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var search = SavedSearch.Create(_alice, "all", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
            search.CreatedAt = start;
            db.SavedSearches.Add(search);
            for (var i = 0; i < 12; i++)
            {
                var property = Property.Create($"{_marker} p{i}", "وصف كافٍ للإعلان", _bob, ListingType.ForSale);
                property.CreatedAt = start.AddMinutes(10 + i);
                db.Properties.Add(property);
            }

            await db.SaveChangesAsync();
        }

        var service = new SavedSearchMatchHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<SavedSearchMatchHostedService>>());

        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(10, await CountMatchesAsync());

        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(12, await CountMatchesAsync());

        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(12, await CountMatchesAsync());
    }

    private async Task<int> CountMatchesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Notifications.CountAsync(n => n.RecipientId == _alice && n.Type == NotificationType.SavedSearchMatch);
    }
}
