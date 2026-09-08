using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Users;
using PropertyApi.Integration.Tests.Investments;

namespace PropertyApi.Integration.Tests.Users;

/// <summary>
/// Real-Postgres regression coverage for Finding F7's sweep
/// (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): a due request must actually execute
/// (anonymize + soft-delete), a not-yet-due request must be left completely untouched, and a
/// cancelled request must never be picked up even if its (cleared) schedule was once in the
/// past. Constructed directly (same pattern as AuditLogRetentionHostedServiceTests) rather than
/// resolved via DI, since AddHostedService&lt;T&gt; registers T only under IHostedService, not as
/// itself -- everything it is built from here (IServiceScopeFactory, IConfiguration,
/// IHostEnvironment) is still the real one the actual host would use, wired through
/// InvestmentApiTestFactory's real <c>Program</c> host.
/// </summary>
public sealed class AccountDeletionSweepTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Sweep executes a due request and leaves a not-yet-due one untouched")]
    public async Task Sweep_ExecutesDueRequest_LeavesNotYetDueRequestAlone()
    {
        var dueUser = await _factory.SeedUserAsync("sweep-due");
        var notYetDueUser = await _factory.SeedUserAsync("sweep-not-due");

        var now = DateTime.UtcNow;

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var due = await db.UserAccounts.SingleAsync(a => a.Id == dueUser.Id);
            due.RequestDeletion(TimeSpan.FromDays(30), now.AddDays(-31));
            // RequestDeletion measures the schedule from "now" at call time -- backdate the
            // clock argument itself so DeletionScheduledFor lands in the past without needing
            // to fake TimeProvider for the whole sweep.

            var notYetDue = await db.UserAccounts.SingleAsync(a => a.Id == notYetDueUser.Id);
            notYetDue.RequestDeletion(TimeSpan.FromDays(30), now);

            await db.SaveChangesAsync();
        });

        var executed = await _factory.InScopeAsync(async services =>
        {
            var sweep = BuildSweep(services);
            return await sweep.SweepAsync(CancellationToken.None);
        });

        Assert.Equal(1, executed);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var due = await db.UserAccounts.AsNoTracking().SingleAsync(a => a.Id == dueUser.Id);
            Assert.True(due.IsDeleted);
            Assert.Equal("Deleted", due.FirstName);

            var notYetDue = await db.UserAccounts.AsNoTracking().SingleAsync(a => a.Id == notYetDueUser.Id);
            Assert.False(notYetDue.IsDeleted);
            Assert.True(notYetDue.HasPendingDeletionRequest);
            Assert.Equal("Test", notYetDue.FirstName);
        });
    }

    [Fact(DisplayName = "Sweep never touches a cancelled request even if its old schedule was in the past")]
    public async Task Sweep_NeverTouchesACancelledRequest()
    {
        var user = await _factory.SeedUserAsync("sweep-cancelled");
        var now = DateTime.UtcNow;

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var account = await db.UserAccounts.SingleAsync(a => a.Id == user.Id);

            account.RequestDeletion(TimeSpan.FromDays(30), now.AddDays(-31));
            account.CancelDeletionRequest(now);

            await db.SaveChangesAsync();
        });

        var executed = await _factory.InScopeAsync(async services =>
        {
            var sweep = BuildSweep(services);
            return await sweep.SweepAsync(CancellationToken.None);
        });

        Assert.Equal(0, executed);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var account = await db.UserAccounts.AsNoTracking().SingleAsync(a => a.Id == user.Id);

            Assert.False(account.IsDeleted);
            Assert.Equal("Test", account.FirstName);
        });
    }

    private static AccountDeletionSweepHostedService BuildSweep(IServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<IHostEnvironment>(),
            services.GetRequiredService<ILogger<AccountDeletionSweepHostedService>>());
}
