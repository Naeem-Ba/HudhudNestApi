using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Auth.Services;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// The re-verification reminder worker runs hourly on every API instance against real PostgreSQL. These
/// tests run its tick for real: more than one page of users, twice in a row, two instances at once, and a
/// user whose data is inconsistent, which must not stop everybody else's reminders.
/// </summary>
public sealed class PhoneVerificationWorkerTests : IClassFixture<PhoneLoginAuditFactory>, IAsyncLifetime
{
    private static int _seed = Environment.TickCount & 0xFFFFFF;
    private readonly PhoneLoginAuditFactory _factory;

    public PhoneVerificationWorkerTests(PhoneLoginAuditFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private PhoneVerificationHostedService NewWorker() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        _factory.Services.GetRequiredService<IConfiguration>(),
        _factory.Services.GetRequiredService<IHostEnvironment>(),
        NullLogger<PhoneVerificationHostedService>.Instance);

    /// <summary>Verified 173 days ago: due in 7 days, so the worker moves them to DueSoon and notifies once.</summary>
    private async Task<List<Guid>> SeedUsers(int count, bool inconsistent = false, int verifiedDaysAgo = 173,
        PhoneVerificationState storedState = PhoneVerificationState.Verified)
    {
        var ids = new List<Guid>();
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var phone = $"+9639{Interlocked.Increment(ref _seed) % 100_000_000:D8}";
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = phone,
                PhoneNumber = phone,
                NormalizedPhoneNumber = phone,
                PhoneNumberConfirmed = true,
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime,
                PhoneLastVerifiedAtUtc = now.AddDays(-verifiedDaysAgo),
                PhoneVerificationDueAtUtc = inconsistent ? null : now.AddDays(180 - verifiedDaysAgo),
                PhoneVerificationGraceEndsAtUtc = inconsistent ? null : now.AddDays(183 - verifiedDaysAgo),
                PhoneVerificationState = storedState
            };
            var created = await users.CreateAsync(user);
            Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Code)));
            db.UserAccounts.Add(UserAccount.Create(user.Id, "Worker", "Test", now.UtcDateTime));
            ids.Add(user.Id);
        }
        await db.SaveChangesAsync();
        return ids;
    }

    private async Task<Dictionary<Guid, int>> ReminderCounts(IEnumerable<Guid> ids)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = ids.ToList();
        var counts = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.Type == NotificationType.PhoneVerification && list.Contains(n.RecipientId))
            .GroupBy(n => n.RecipientId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return list.ToDictionary(id => id, id => counts.FirstOrDefault(c => c.Key == id)?.Count ?? 0);
    }

    private async Task<int> CountInState(IEnumerable<Guid> ids, PhoneVerificationState state)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = ids.ToList();
        return await db.Users.CountAsync(u => list.Contains(u.Id) && u.PhoneVerificationState == state);
    }

    [Fact]
    public async Task MoreThanOnePage_EveryUserGetsExactlyOneReminder_AndASecondTickAddsNone()
    {
        var ids = await SeedUsers(250);

        await NewWorker().ProcessAsync(CancellationToken.None);
        var first = await ReminderCounts(ids);
        await NewWorker().ProcessAsync(CancellationToken.None);
        var second = await ReminderCounts(ids);

        Assert.Equal(250, await CountInState(ids, PhoneVerificationState.DueSoon));
        Assert.All(first.Values, c => Assert.Equal(1, c));
        Assert.All(second.Values, c => Assert.Equal(1, c));
    }

    [Fact]
    public async Task TwoInstancesAtTheSameTime_NeverDuplicateAReminder()
    {
        var ids = await SeedUsers(120);

        await Task.WhenAll(
            NewWorker().ProcessAsync(CancellationToken.None),
            NewWorker().ProcessAsync(CancellationToken.None));

        var counts = await ReminderCounts(ids);
        Assert.All(counts.Values, c => Assert.Equal(1, c));
        Assert.Equal(120, await CountInState(ids, PhoneVerificationState.DueSoon));
    }

    [Fact]
    public async Task OneInconsistentUser_DoesNotStopEveryoneElsesReminders()
    {
        var healthy = await SeedUsers(30);
        var poison = await SeedUsers(1, inconsistent: true);

        var tick = await Record.ExceptionAsync(() => NewWorker().ProcessAsync(CancellationToken.None));

        Assert.Null(tick);
        Assert.Equal(30, await CountInState(healthy, PhoneVerificationState.DueSoon));
        Assert.All((await ReminderCounts(healthy)).Values, c => Assert.Equal(1, c));
        // the inconsistent row is repaired and reminded like everyone else
        Assert.All((await ReminderCounts(poison)).Values, c => Assert.Equal(1, c));
    }

    [Fact]
    public async Task FarFromDue_StaleStateIsCorrected_AndNobodyIsReminded()
    {
        // The tick only reads users who have something to do; a stored state that no longer matches the
        // dates (re-verified since the last tick) is one of those and must still be put back to Verified.
        var stale = await SeedUsers(5, verifiedDaysAgo: 10, storedState: PhoneVerificationState.DueSoon);
        var settled = await SeedUsers(5, verifiedDaysAgo: 10);

        await NewWorker().ProcessAsync(CancellationToken.None);

        Assert.Equal(5, await CountInState(stale, PhoneVerificationState.Verified));
        Assert.Equal(5, await CountInState(settled, PhoneVerificationState.Verified));
        Assert.All((await ReminderCounts(stale.Concat(settled))).Values, c => Assert.Equal(0, c));
    }
}
